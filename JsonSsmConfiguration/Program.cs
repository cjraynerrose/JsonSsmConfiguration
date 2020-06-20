using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Amazon.Runtime.Internal.Util;
using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Internal;
using Amazon.SimpleSystemsManagement.Model;
using Newtonsoft.Json.Linq;
using ThirdParty.BouncyCastle.Asn1;

namespace JsonSsmConfiguration
{
    public class Program
    {
        public static AmazonSimpleSystemsManagementClient client = new AmazonSimpleSystemsManagementClient();

        public static async Task Main(string[] args)
        {
            PrintHelp();
            await Menu();
        }

        private static async Task Menu()
        {
            string[] input;
            var exit = false;
            do
            {
                Console.Write("SSM<>JSON: ");
                input = Console.ReadLine().Split(" ");
                switch (input[0].ToLowerInvariant())
                {
                    case "get":
                        if (input.Length == 3)
                            await Get(input[1], input[2]);
                        if (input.Length == 2)
                            await Get(input[1], null);
                        break;
                    case "put":
                        await Put(input[1]);
                        break;
                    case "exit":
                        exit = true;
                        break;
                    default:
                        PrintHelp();
                        break;
                }
            }
            while (!exit);
            
        }

        private static void PrintHelp()
        {
            var helpText = File.ReadAllLines("Files/Helptext.txt");
            foreach(var line in helpText)
                Console.WriteLine(line);
        }

        private static async Task Put(string filePath)
        {
            var jsonData = File.ReadAllText(filePath);

            var data = JObject.Parse(jsonData);
            //var data = JObject.Parse(@"{""ENCRYPT"":[""/test/Development/case"",""/test/Development/somepath/somevalue""],""test"":{""Development"":{""somepath"":{""somevalue"":""valuevalue""},""anotherpath"":{""anothervalue"":369,""array"":[""val1"",""val2"",""val3""]},""case"":[{""id"":0},{""id"":1},{""id"":2}]}}}");
            var pathsToEncrypt = new List<JToken>();
            if(data.ContainsKey("ENCRYPT"))
            {
                pathsToEncrypt = data
                    .SelectTokens("ENCRYPT")
                    .Children()
                    .ToList();

                data.Remove("ENCRYPT");
            }

            var parameters = FlattenAndFormatJson(data, out var stringLists);

            var requests = new PutParameterRequest[parameters.Count];
            var i = 0;
            foreach (var param in parameters)
            {
                var paramType = ParameterType.String;
                var encrypt = pathsToEncrypt.Any(pte => param.Key.StartsWith(pte.ToString()));
                if(!encrypt)
                {
                    var list = stringLists.Any(sl => param.Key.Equals(sl));
                    if (list) paramType = ParameterType.StringList;
                }
                else
                {
                    paramType = ParameterType.SecureString;
                }

                // TODO add options for this stuff that can be passed in
                requests[i] = new PutParameterRequest
                {
                    Name = param.Key,
                    Value = param.Value,
                    DataType = "text",
                    Overwrite = true,
                    Tier = ParameterTier.Standard,
                    Type = paramType,
                };

                i++;
            }

            foreach (var request in requests)
            {
                var response = await client.PutParameterAsync(request);
                if(response.HttpStatusCode.ToString() != "OK")
                {
                    Console.WriteLine($"{response.HttpStatusCode} - Response status code does not indicat success" +
                        $" for the parameter {request.Type} {request.Name}.");
                }
                else
                {
                    Console.WriteLine($"{response.HttpStatusCode} - Success for parameter {request.Type} {request.Name}");
                }
            }
        }

        private static Dictionary<string,string> FlattenAndFormatJson(JObject data, out List<string> listType)
        {
            IEnumerable<JToken> jTokens = data.Descendants().Where(p => p.Count() == 0);

            var stringLists = new Dictionary<string, string>();
            Dictionary<string, string> results = jTokens.Aggregate(new Dictionary<string, string>(), (properties, jToken) =>
            {
                var path = $"/{jToken.Path}";
                path = path
                    .Replace('.', '/')
                    .Replace('[', '/')
                    .Replace("]", "");

                // Yes, this is will cause a failure down the line if the index is >9
                // TODO add logic to find the length of the number and remove appropriate characters
                if (char.IsDigit(path[^1]))
                {
                    path = path[0..^2];

                    var exists = stringLists.Any(x => x.Key == path);
                    if(!exists)
                    {
                        stringLists.Add(path, jToken.ToString());
                    }
                    else
                    {
                        stringLists[path] += $",{jToken}";
                    }
                }
                else
                {
                    properties.Add(path, jToken.ToString());
                }

                return properties;
            });

            var merged = results
                .Concat(stringLists)
                .OrderBy(k => k.Key)
                .ToDictionary(k => k.Key, v => v.Value);

            //foreach (var item in merged)
            //{
            //    Console.WriteLine($"{item.Key}  :  {item.Value}");
            //}

            listType = stringLists.Select(kvp => kvp.Key).ToList();
            return merged;
        }

        private static async Task Get(string path, string outputPath)
        {

            var request = new GetParametersByPathRequest
            {
                Path = path,
                Recursive = true,
                WithDecryption = true
            };

            var parameters = await RequestParametersRecursive(request);

            var config = ConvertResponseToJson(parameters);
            Console.WriteLine(config.ToString());
            if(!string.IsNullOrWhiteSpace(outputPath))
                File.WriteAllText(outputPath, config.ToString());
        }

        private static async Task<List<Parameter>> RequestParametersRecursive(GetParametersByPathRequest request)
        {
            List<Parameter> parameters = new List<Parameter>();
            bool hasToken = false;
            do
            {
                var response = await RequestParameters(request);
                if (response.HttpStatusCode.ToString() != ("OK"))
                    throw new Exception("Went Wrong");

                parameters.AddRange(response.Parameters);

                if(!string.IsNullOrWhiteSpace(response.NextToken))
                {
                    hasToken = true;
                    request.NextToken = response.NextToken;
                }
                else
                {
                    hasToken = false;
                }
            }
            while (hasToken);

            return parameters;
        }
        private static async Task<GetParametersByPathResponse> RequestParameters(GetParametersByPathRequest request)
        {

            GetParametersByPathResponse response;
            try
            {
                response = await client.GetParametersByPathAsync(request);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }

            return response;
        }

        private static JObject ConvertResponseToJson(List<Parameter> parameters)
        {
            var rows = new List<JObject>();
            var secureStrings = new List<string>();

            foreach (var param in parameters)
            {
                if(param.Type == ParameterType.SecureString)
                {
                    secureStrings.Add(param.Name);
                }

                // Find correct number of } to append
                var braceCount = param.Name.Count(p => p == '/');
                var append = "";
                for (int i = 0; i < braceCount; i++) append += "}";

                //Remove leading '/'. Will be replaced with {" later.
                var path = param.Name.Substring(1);

                var seperator = "\":{\""; 
                path = path.Replace("/", seperator);

                var prepender = "{\"";
                var requireQuotes = ResolveValueTypeRequiresQuotes(param.Value);

                string finalSeperator;
                string finalCloser;
                string value = param.Value;
                if (param.Type == ParameterType.StringList)
                {
                    finalSeperator = "\":[\"";
                    finalCloser = "\"]";
                    value = value.Replace(",", "\",\"");
                }
                else
                {
                    finalSeperator = requireQuotes ? "\":\"" : "\":";
                    finalCloser = requireQuotes ? "\"" : "";
                }

                // Add leading {" then add the value with a seperator and append correct number of }
                path = $"{prepender}{path}{finalSeperator}{value}{finalCloser}{append}";

                try
                {
                    rows.Add(JObject.Parse(path));
                }
                catch (Exception e)
                {
                    throw e;
                }
            }

            var json = rows[0];
            var jsonSettings = new JsonMergeSettings
            {
                MergeArrayHandling = MergeArrayHandling.Union
            };
            for(int i=1;i<rows.Count;i++)
            {
                json.Merge(rows[i], jsonSettings);
            }

            return json;
        }

        /// <summary>
        /// This will use regex to decide if a value need be enclosed in quotes.
        /// If the value is a StringList then it will return true.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        private static bool ResolveValueTypeRequiresQuotes(string value)
        {
            var boolCheck = new Regex(@"(true|false)", RegexOptions.IgnoreCase);
            var numCheck = new Regex(@"^-?[0-9][0-9,\.]+$");

            var isBool = boolCheck.IsMatch(value);
            var isNum = numCheck.IsMatch(value);
            if (isBool || isNum)
                return false;
            return true;
        }

        private static void PrintResponse(GetParametersByPathResponse response)
        {
            if(response.HttpStatusCode.ToString() != "OK")
            {
                Console.WriteLine($"Response status code does not indicate success: {response.HttpStatusCode}");
                return;
            }

            foreach(var param in response.Parameters)
            {
                Console.WriteLine($"{param.Name}:{param.Value}");
            }
        }
    }
}
