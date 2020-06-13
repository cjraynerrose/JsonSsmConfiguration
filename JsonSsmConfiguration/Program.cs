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

            var request = new GetParametersByPathRequest
            { 
                Path = "/common-config/Development/Serilog",
                Recursive = true,
                WithDecryption = true
            };

            var response = await client.GetParametersByPathAsync(request);

            var put = new PutParameterRequest
            {
                
            };

            ConvertResponseToJson(response.Parameters);
            PrintResponse(response);
        }

        private static void ConvertJsonToSsm(JObject json)
        {

        }

        private static void ConvertResponseToJson(List<Parameter> parameters)
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
                Console.WriteLine(path);
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

            Console.WriteLine(json.ToString());
        }

        /// <summary>
        /// This will use regex to decide if a value need be enclosed in quotes.
        /// If the value is a StringList then it will return true.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        private static bool ResolveValueTypeRequiresQuotes(string value)
        {
            var boolCheck = new Regex(@"(true|false)");
            var numCheck = new Regex(@"^-?[0-9][0-9,\.]+$");

            var isBool = boolCheck.IsMatch(value);
            var isNum = numCheck.IsMatch(value);
            if (isBool || isNum)
                return false;
            return true;
        }

        private static void PrintResponse(GetParametersByPathResponse response)
        {
            if(!response.HttpStatusCode.ToString().StartsWith("OK"))
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
