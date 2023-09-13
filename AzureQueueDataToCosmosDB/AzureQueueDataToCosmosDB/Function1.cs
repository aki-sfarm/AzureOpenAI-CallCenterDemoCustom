using System;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.WebJobs.Extensions.CosmosDB;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;

namespace AzureQueueDataToCosmosDB
{
    public class Function1
    {
        [FunctionName("ProcessCallCenterDataQueMessage")]
        public async Task Run([QueueTrigger("sbgtm-oa-callcenter-app", Connection = "AzureWebJobsStorage")]string myQueueItem, ILogger log)
        {
            var connectionString = Environment.GetEnvironmentVariable("CosmosDBConnectionString");
            var databaseName = Environment.GetEnvironmentVariable("CosmosDBDatabaseName");
            var containerName = Environment.GetEnvironmentVariable("CosmosDBContainerName");

            if (string.IsNullOrEmpty(connectionString) || string.IsNullOrEmpty(databaseName) || string.IsNullOrEmpty(containerName))
            {
                log.LogInformation("Could not retrieve the required information from the environment variables.");
                return;
            }

            var regex = new Regex(@"\\u[0-9a-fA-F]{4}");
            var str_QueueMessage = "";
            if (regex.IsMatch(myQueueItem))
            {
                str_QueueMessage = Regex.Unescape(myQueueItem);
            }
            else
            {
                str_QueueMessage = myQueueItem;
            }
                


            log.LogInformation(connectionString);
            log.LogInformation(databaseName);
            log.LogInformation(containerName);
            log.LogInformation($"C# Queue trigger function processed: {str_QueueMessage}");


            using var client = new CosmosClient(connectionString);


            var database = client.GetDatabase(databaseName);
            var container = database.GetContainer(containerName);

            dynamic item = null;


            try
            {
                item = JsonConvert.DeserializeObject<dynamic>(str_QueueMessage);

                if (!item.ContainsKey("id") || string.IsNullOrEmpty(item["id"].ToString()))
                {
                    item["id"] = Guid.NewGuid().ToString();
                    log.LogInformation("Add id to json");
                }

            }
            catch (JsonException jsonEx)
            {
                log.LogError($"Failed to deserialize the message. Error: {jsonEx.Message}");
            }
            catch (Exception ex)
            {
                log.LogError($"An error occurred during registration to CosmosDB. Error: {ex.Message}");
            }


            log.LogInformation($"JSON to be saved to CosmosDB: {JsonConvert.SerializeObject(item)}");


            try
            {

                await container.CreateItemAsync(item, new PartitionKey(item.id.ToString()));

            }
            catch (Exception ex)
            {
                log.LogError("An error occurred during registration to CosmosDB.");
                log.LogError(ex.Message);
            }

            log.LogInformation("Data has been registered to CosmosDB.");

        }
    }


}
