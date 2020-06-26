using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JsonSsmConfiguration
{
    public class Put : IRequest
    {
        private AmazonSimpleSystemsManagementClient _client;

        public Put(AmazonSimpleSystemsManagementClient client)
        {
            _client = client;
        }

        public async Task Request(string[] input)
        {
            var filePath = input[1];

            var jsonData = File.ReadAllText(filePath);

            var data = JObject.Parse(jsonData);
            //var data = JObject.Parse(@"{""ENCRYPT"":[""/test/Development/case"",""/test/Development/somepath/somevalue""],""test"":{""Development"":{""somepath"":{""somevalue"":""valuevalue""},""anotherpath"":{""anothervalue"":369,""array"":[""val1"",""val2"",""val3""]},""case"":[{""id"":0},{""id"":1},{""id"":2}]}}}");
            var pathsToEncrypt = new List<JToken>();
            if (data.ContainsKey("ENCRYPT"))
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
                if (!encrypt)
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
                var response = await _client.PutParameterAsync(request);
                if (response.HttpStatusCode.ToString() != "OK")
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
    }
}
