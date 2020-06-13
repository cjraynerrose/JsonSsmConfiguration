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

namespace JsonSsmConfiguration
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var client = new AmazonSimpleSystemsManagementClient();

            //await Get(client);
            await Put(client);
        }

        private static async Task Put(AmazonSimpleSystemsManagementClient client)
        {
            var data = JObject.Parse(@"{""test"":{""Development"":{""somepath"":{""somevalue"":""valuevalue""},""anotherpath"":{""anothervalue"":369,""array"":[""val1"",""val2"",""val3""]},""case"":[{""id"":0},{""id"":1},{""id"":2}]}}}");
            var parameters = FlattenAndFormatJson(data);

            var requests = new PutParameterRequest[parameters.Count];
            var i = 0;
            foreach (var param in parameters)
            {
                requests[i] = new PutParameterRequest
                {
                    Name = param.Key,
                    Value = param.Value,
                    DataType = "text",
                    Overwrite = true,
                    Tier = ParameterTier.Standard,
                    Type = ParameterType.String
                };

                i++;
            }

            foreach (var request in requests)
            {
                var response = await client.PutParameterAsync(request);
                if(response.HttpStatusCode.ToString() != "OK")
                {
                    Console.WriteLine($"{response.HttpStatusCode} - Response status code does not indicat success" +
                        $" for the parameter {request.Name}.");
                }
                else
                {
                    Console.WriteLine($"{response.HttpStatusCode} - Success for parameter {request.Name}");
                }
            }
        }

        private static Dictionary<string,string> FlattenAndFormatJson(JObject data)
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
                if (char.IsDigit(path[path.Length - 1]))
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

            foreach (var item in merged)
            {
                Console.WriteLine($"{item.Key}  :  {item.Value}");
            }

            return merged;
        }
        private static void ConvertJsonToSsm(JObject json)
        {

        }

        private static async Task Get(AmazonSimpleSystemsManagementClient client)
        {
            var request = new GetParametersByPathRequest
            {
                Path = "/common-config/Development/Serilog",
                Recursive = true,
                WithDecryption = true
            };

            var response = await client.GetParametersByPathAsync(request);

            var config = ConvertResponseToJson(response.Parameters);
            Console.WriteLine(config.ToString());
        }

        private static JObject ConvertResponseToJson(List<Parameter> parameters)
        {
            var rows = new List<JObject>();

            foreach (var param in parameters)
            {
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
                rows.Add(JObject.Parse(path));
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
