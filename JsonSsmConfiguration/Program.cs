using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
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
                Path = "/identity/Development/",
                Recursive = true,
                WithDecryption = true
            };

            var response = await client.GetParametersByPathAsync(request);

            ConvertResponseToJson(response.Parameters);
            //PrintResponse(response);
        }

        private static void ConvertResponseToJson(List<Parameter> parameters)
        {
            var rows = new List<JObject>();

            foreach (var param in parameters)
            {
                var braceCount = param.Name.Count(p => p == '/');
                var append = "";
                for (int i = 0; i < braceCount; i++) append += "}";

                //Remove leading '/'. Will be replaced with {" later.
                var path = param.Name.Substring(1);

                var thing = "\":{\""; // ":{"
                path = path.Replace("/", thing);
                path = "{\"" + path + "\":\"" + param.Value + "\"" + append;

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
