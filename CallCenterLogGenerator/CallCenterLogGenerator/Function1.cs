using System;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Azure.AI.OpenAI;
using Azure;
using Newtonsoft.Json.Linq;
using Azure.Storage.Queues;
using System.Text;
using Microsoft.Extensions.Options;
using System.Threading.Tasks;
using CallCenterLogGenerator;
using Microsoft.Extensions.Configuration;
using System.IO;





namespace CallCenterLogGenerator
{
    public class Function1
    {
        [FunctionName("CallCenterLogDataGenerator")]
        public async Task Run([TimerTrigger("0 */1 * * * *")]TimerInfo myTimer, ILogger log)
        {

            var options = OpenAIOptions.GetOptions();

            

            var client = new OpenAIClient(new Uri(options.Endpoint), new AzureKeyCredential(options.ApiKey)); 

            

            string ConnectionString = Environment.GetEnvironmentVariable("AzureStorageUri");
            string QueueName = Environment.GetEnvironmentVariable("AzureStorageQueueName");

            

            // 現在の日本の時間を取得
            TimeZoneInfo jstZone = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
            DateTime currentTimeInJST = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, jstZone);

            // ランダムな値を生成
            Random random = new Random();
            double randomValue = random.NextDouble(); // 0.0 から 1.0 までのランダムな数値

            var str_CallSituation = "";
            var str_CallLog = "";

            // 確率の基準を設定
            //7時から22時までの確率は5%、それ以外は0.8%とする
            //double threshold = (currentTimeInJST.Hour >= 7 && currentTimeInJST.Hour < 22) ? 0.3 : 0.008;
            double threshold = (currentTimeInJST.Hour >= 7 && currentTimeInJST.Hour < 22) ? 0.05 : 0.008;


            // メイン処理の実行判定
            if (randomValue < threshold)
            {
                log.LogInformation("OpenAI URL:" + options.Endpoint.ToString());
                //log.LogInformation(options.ApiKey.ToString());

                //log.LogInformation(":Queue ConnectionString:" + ConnectionString);
                log.LogInformation("QueueName:" + QueueName);

                random = new Random();
                var randomNumber = random.Next(1, 101); // Generates a number between 1 and 100

                //電話内容のシチュエーションをランダムに選択
                if (randomNumber <= 30)
                {
                    //30%の確率で予約に関する電話を想定
                    str_CallSituation = "今回は、予約に関する電話を想定してください。";
                }
                else if (randomNumber <= 40)
                {
                    //10%の確率で変更に関する電話を想定
                    str_CallSituation = "今回は、予約の変更に関する電話を想定してください。";
                }
                else if (randomNumber <= 45)
                {
                    //5%の確率で予約キャンセルに関する電話を想定
                    str_CallSituation = "今回は、予約のキャンセルに関する電話を想定してください。";
                }
                else if (randomNumber <= 95)
                {
                    //50%の確率で問い合わせに関する電話を想定
                    str_CallSituation = "今回は、問い合わせに関する電話を想定してください。問い合わせの場合、予約などは発生しない状況を想定してください。";

                }
                else if (randomNumber <= 98)
                {
                    //3%の確率でクレームに関する電話を想定
                    str_CallSituation = "今回は、クレームに関する電話を想定してください。";

                }
                else
                {
                    //2%の確率でセールスなどホテル業務に無関係な電話を想定
                    str_CallSituation = "今回は、セールスなどホテル業務に無関係な電話を想定してください。";

                }


                var chatCompletionsOptions = new ChatCompletionsOptions
                {
                    MaxTokens = 300,
                    Messages =
                    {
                        new ChatMessage(ChatRole.System,
                        "あなたは、サンプルデータの作成を手伝います。" +
                        "架空のホテルの受付電話を想定し対応してください。"
                        )
                    }
                };

                try 
                { 
                    await client.GetChatCompletionsAsync(options.DeploymentName, chatCompletionsOptions); 
                }
                catch(Exception ex)
                {
                    log.LogInformation($"Error: Fail send System Message to ChatGpt");
                    log.LogInformation($"Error: {ex.Message}");
                }
                

                var userMessage = "こんにちは。" +
                    "まずは、架空のホテルの受付電話のやり取りのログを150文字程度で生成してください。ホテル名は、AIDemoホテルです。電話の内容は多種多様で予約や変更、クレームなどがあります。" +
                    str_CallSituation;

                chatCompletionsOptions.Messages.Add(new ChatMessage(ChatRole.User, userMessage));

                log.LogInformation($"{ChatRole.User}: {userMessage}");

                var response = await client.GetChatCompletionsAsync(options.DeploymentName, chatCompletionsOptions);

                str_CallLog = response.Value.Choices[0].Message.Content;

                log.LogInformation($"{ChatRole.System}: {response.Value.Choices[0].Message.Content}");


                //生成された電話ログを基に評価し、jsonを作成するように指示
                userMessage = "あなたが生成した架空の電話ログを基に、解析し以下の項目があるjsonを作成してください。" +
                        "応答はjsonのみで返してください。" +
                        "作ってほしいサンプルデータは、ホテルの受付電話のログから生成された結果です。" +
                        "jsonには次の項目が入ります。「summary」「sentimentScore」「keyEntities」「topic」" +
                        "summaryには、会話の要約が入ります。" +
                        "sentimentScoreには、会話の感情スコアが入ります。感情スコアは0から100の範囲で表現されます。" +
                        "keyEntitiesには、会話のキーエンティティが入ります。これは、複数の場合があります。" +
                        "topicには、会話のトピックが入ります。これは、複数の場合があります。" +
                        "内容が予約に関する場合は、「予約」、" +
                        "内容が予約の変更に関する場合は、「予約の変更」、" +
                        "内容が予約のキャンセルに関する場合は、「キャンセル」、" +
                        "内容が問い合わせに関する場合は、「問い合わせ」、" +
                        "内容がクレームに関する場合は、「クレーム」を" +
                        "topicに追加してください。" +
                        "\\n" +
                        "jsonのサンプルを送ります。この形式で作成してください。" +
                        "\\n" +
                        "{\"summary\":\"Sample Summary\",\"sentimentScore\":80,\"keyEntities\":[\"キャンセル\",\"料金\"],\"topic\":[\"キャンセル\",\"料金情報\"],\"CallLog\":\"Sample Log\"}" +
                        "\\n" +
                        "あなたが生成した架空の電話ログは下記です。" +
                        "\\n" +
                        "\\n" +
                        str_CallLog;


                chatCompletionsOptions.Messages.Add(new ChatMessage(ChatRole.User, userMessage));

                log.LogInformation($"{ChatRole.User}: {userMessage}");

                response = await client.GetChatCompletionsAsync(options.DeploymentName, chatCompletionsOptions);

                log.LogInformation($"{ChatRole.System}: {response.Value.Choices[0].Message.Content}");

                //json部分を抽出
                string str_response = response.Value.Choices[0].Message.Content;
                int startIndex = str_response.IndexOf("{");
                int lastIndex = str_response.LastIndexOf("}");

                if (startIndex != -1 && lastIndex != -1)
                {
                    string extracted = str_response.Substring(startIndex, (lastIndex - startIndex) + 1);

                    var str_Json = extracted;

                    log.LogInformation("Received JSON from OpenAI.");

                    //jsonに日時を追加
                    log.LogInformation("Add DateTime to JSON.");
                    DateTime utcNow = DateTime.UtcNow;
                    DateTime jstNow = utcNow.AddHours(9); // JST is UTC+9

                    JObject jsonObject = JObject.Parse(str_Json);

                    jsonObject["dateTime"] = jstNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

                    log.LogInformation("Add CallLog to JSON.");
                    jsonObject["CallLog"] = str_CallLog;

                    str_Json = jsonObject.ToString();

                    


                    var str_base64Json = Convert.ToBase64String(Encoding.UTF8.GetBytes(str_Json));

                    var queueClient = new QueueClient(ConnectionString, QueueName);

                    // Ensure the queue exists.
                    queueClient.CreateIfNotExists();


                    try
                    {
                        // Send a message to the queue.
                        queueClient.SendMessage(str_base64Json);

                        log.LogInformation("Message sent to the Azure Storage Queue.");

                    }
                    catch (Exception ex)
                    {
                        log.LogInformation("Error: Fail sent to the Azure Storage Queue.");
                        log.LogInformation(ex.Message);
                    }
                }
                else
                {
                    //jsonが生成されなかった場合
                    log.LogInformation("Error: Fail to generate json.");

                }

            }
            else
            {
                log.LogInformation($"The processing was not executed due to probability.");
            }
        }


    }
    //public class OpenAIOptions
    //{
    //    public string Endpoint { get; set; }
    //    public string ApiKey { get; set; }
    //    public string DeploymentName { get; set; }

    //    public static OpenAIOptions ReadFromLocalSettings()
    //    {
    //        var builder = new ConfigurationBuilder()
    //            .SetBasePath(Directory.GetCurrentDirectory())
    //            .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true);
    //        var configuration = builder.Build();
    //        return configuration.GetSection(nameof(OpenAIOptions)).Get<OpenAIOptions>();
    //    }

    //}

    public class OpenAIOptions
    {
        public string Endpoint { get; set; }
        public string ApiKey { get; set; }
        public string DeploymentName { get; set; }

        public static OpenAIOptions GetOptions()
        {
            if (IsRunningOnAzure())
            {
                return ReadFromEnvironment();
            }
            else
            {
                return ReadFromLocalSettings();
            }
        }

        private static bool IsRunningOnAzure()
        {
            // Azure Functionsの環境で実行されているかを判定する。
            // 'WEBSITE_SITE_NAME'はAzure Functionsの環境変数
            return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME"));
        }

        private static OpenAIOptions ReadFromLocalSettings()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true);
            var configuration = builder.Build();
            return configuration.GetSection(nameof(OpenAIOptions)).Get<OpenAIOptions>();
        }

        private static OpenAIOptions ReadFromEnvironment()
        {
            var builder = new ConfigurationBuilder()
                .AddEnvironmentVariables();
            var configuration = builder.Build();
            return configuration.GetSection(nameof(OpenAIOptions)).Get<OpenAIOptions>();
        }
    }
}
