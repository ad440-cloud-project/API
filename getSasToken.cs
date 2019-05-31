using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Net.Http;
using System.Linq;
using System.Net;
using System.Configuration;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.KeyVault.Models;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using System.Text;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Auth;


namespace Functions
{
    //Class to get an SAS Token. 
    public static class GetAccountSASToken
    {
        [FunctionName("getSasToken")]
        [Consumes("application/json")]
        [Produces("application/json")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequestMessage req,
            ExecutionContext context,
            ILogger log)
        {
            if (req.Method == HttpMethod.Post) {
                return (ActionResult)new StatusCodeResult(405);
            }

            //Setup a ConfigurationBuilder to pull config values from Application Settings.
            var config = new ConfigurationBuilder()
                        .SetBasePath(context.FunctionAppDirectory)
                        .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                        .AddEnvironmentVariables()
                        .Build();

            //Setup an Azure Service Token Provider as a part of gaining access to the Key Vault.
            AzureServiceTokenProvider azureServiceTokenProvider = new AzureServiceTokenProvider();

            //Setup Cloud Storage Account and Blob Container Objects for use in container retrieval
            CloudStorageAccount storageAccount = null;
            CloudBlobContainer cloudBlobContainer = null;

            try
            {
                // Create a connection to the Key Vault
                var keyVaultClient = new KeyVaultClient(
                    new KeyVaultClient.AuthenticationCallback(azureServiceTokenProvider.KeyVaultTokenCallback));

                //Pull the SecretsBundle containing the Storage Connection String from the Key Vault.
                var secret = await keyVaultClient.GetSecretAsync($"{config["KEY_VAULT_URI"]}secrets/{config["STORAGE_CONNECTION"]}/");

                //The Storage Connection String is stored in the Secret Bundle as "Value". The following gets
                //that value so we can use it in SAS generation. A secret bundle contains more information, but
                //we only care about the value.
                string storageConnectionString = secret.Value.ToString();
                log.LogInformation("Secret retreived from key vault.");

                //Check whether the connection string can be parsed
                if (CloudStorageAccount.TryParse(storageConnectionString, out storageAccount))
                {
                    log.LogInformation("Storage connection string is valid.");

                    
                    try
                    {
                        // Create the CloudBlobClient that represents the Blob storage endpoint for the storage account.
                        CloudBlobClient cloudBlobClient = storageAccount.CreateCloudBlobClient();

                        //Grab a reference to the cloud blob container we want to r/w to, in this case "images"
                        cloudBlobContainer = cloudBlobClient.GetContainerReference("images");
                        log.LogInformation("Fetched container reference '{0}'", cloudBlobContainer.Name);

                        //Retrieve the SAS Uri/Token for the images container, put it into a JSON Object, and return the object.
                        string[] result = GetContainerSasUri(cloudBlobContainer);
                        var obj = new {uri = result[0], token = result[1], message = "Pipeline test."/*"SAS Token good for 60 minutes. Token has Read/Write Privileges. File name should be appended between URI and SAS Token on upload."*/ };
                        var jsonToReturn = JsonConvert.SerializeObject(obj, Formatting.Indented);
                        return (ActionResult)new OkObjectResult(jsonToReturn);

                    }
                    catch (StorageException ex)
                    {
                        return new BadRequestObjectResult(ex.Message);
                    }
                

                }
                else
                {
                    // Otherwise, let the user know that they need to add the connection string to the key vault.
                    Console.WriteLine(
                    "A connection string has not been defined in the key vault secrets. " +
                    "Add a secret named 'storageConnectionString' with your storage " +
                    "connection string as the value.");
                    return new BadRequestObjectResult("Specified container does not exist.");
                }

            }   
            //Throw an error if key vault access fails.
            catch (Exception ex) {
                log.LogError(ex.Message);
                return new ForbidResult("Unable to access secrets in vault: " + ex.Message);
            }

        }
            

        private static string[] GetContainerSasUri(CloudBlobContainer container, string storedPolicyName = null)
        {
            string sasContainerToken;
            string[] result = new string[2];

            // If no stored policy is specified, create a new access policy and define its constraints.
            if (storedPolicyName == null)
            {
                // Note that the SharedAccessBlobPolicy class is used both to define the parameters of an ad hoc SAS, and
                // to construct a shared access policy that is saved to the container's shared access policies.
                SharedAccessBlobPolicy adHocPolicy = new SharedAccessBlobPolicy()
                {
                    // When the start time for the SAS is omitted, the start time is assumed to be the time when the storage service receives the request.
                    // Omitting the start time for a SAS that is effective immediately helps to avoid clock skew.
                    SharedAccessExpiryTime = DateTime.UtcNow.AddHours(1),
                    Permissions = SharedAccessBlobPermissions.Write | SharedAccessBlobPermissions.List | SharedAccessBlobPermissions.Read
                };

                // Generate the shared access signature on the container, setting the constraints directly on the signature.
                sasContainerToken = container.GetSharedAccessSignature(adHocPolicy, null);

                Console.WriteLine("SAS for blob container (ad hoc): {0}", sasContainerToken);
                Console.WriteLine();
            }
            else
            {
                // Generate the shared access signature on the container. In this case, all of the constraints for the
                // shared access signature are specified on the stored access policy, which is provided by name.
                // It is also possible to specify some constraints on an ad hoc SAS and others on the stored access policy.
                sasContainerToken = container.GetSharedAccessSignature(null, storedPolicyName);

                Console.WriteLine("SAS for blob container (stored access policy): {0}", sasContainerToken);
                Console.WriteLine();
            }

            // Put the URI and SAS Token into the result array and return.
            result[0] = container.Uri.ToString();
            result[1] = sasContainerToken.ToString();
            return result;
        }
    }
}



