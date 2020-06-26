using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using ThirdParty.BouncyCastle.Asn1;

namespace JsonSsmConfiguration
{
    public class Get : IRequest
    {
        private AmazonSimpleSystemsManagementClient client;

        public Get()
        {

        }

        public Get(AmazonSimpleSystemsManagementClient client)
        {
            this.client = client;
        }

        public async Task Request(string[] input)
        {
            string path = input[1];
            string outputPath;
            if (input.Length == 3)
                outputPath = input[2];

            var request = new GetParametersByPathRequest
            {
                Path = path,
                Recursive = true,
                WithDecryption = true
            };

            var parameters = await RequestParametersRecursive(request);

            var config = ConvertResponseToJson(parameters);
            Console.WriteLine(config.ToString());
            if (!string.IsNullOrWhiteSpace(outputPath))
                File.WriteAllText(outputPath, config.ToString());
        }

        private async Task<List<Parameter>> RequestParametersRecursive(GetParametersByPathRequest request)
        {
            List<Parameter> parameters = new List<Parameter>();
            bool hasToken = false;
            do
            {
                var response = await RequestParameters(request);
                if (response.HttpStatusCode.ToString() != ("OK"))
                    throw new Exception("Went Wrong");

                parameters.AddRange(response.Parameters);

                if (!string.IsNullOrWhiteSpace(response.NextToken))
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
        private async Task<GetParametersByPathResponse> RequestParameters(GetParametersByPathRequest request)
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
    }
}
