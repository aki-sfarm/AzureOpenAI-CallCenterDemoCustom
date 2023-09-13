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

            

            // ���݂̓��{�̎��Ԃ��擾
            TimeZoneInfo jstZone = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
            DateTime currentTimeInJST = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, jstZone);

            // �����_���Ȓl�𐶐�
            Random random = new Random();
            double randomValue = random.NextDouble(); // 0.0 ���� 1.0 �܂ł̃����_���Ȑ��l

            var str_CallSituation = "";
            var str_CallLog = "";

            // �m���̊��ݒ�
            //7������22���܂ł̊m����5%�A����ȊO��0.8%�Ƃ���
            //double threshold = (currentTimeInJST.Hour >= 7 && currentTimeInJST.Hour < 22) ? 0.3 : 0.008;
            double threshold = (currentTimeInJST.Hour >= 7 && currentTimeInJST.Hour < 22) ? 0.05 : 0.008;


            // ���C�������̎��s����
            if (randomValue < threshold)
            {
                log.LogInformation("OpenAI URL:" + options.Endpoint.ToString());
                //log.LogInformation(options.ApiKey.ToString());

                //log.LogInformation(":Queue ConnectionString:" + ConnectionString);
                log.LogInformation("QueueName:" + QueueName);

                random = new Random();
                var randomNumber = random.Next(1, 101); // Generates a number between 1 and 100

                //�d�b���e�̃V�`���G�[�V�����������_���ɑI��
                if (randomNumber <= 30)
                {
                    //30%�̊m���ŗ\��Ɋւ���d�b��z��
                    str_CallSituation = "����́A�\��Ɋւ���d�b��z�肵�Ă��������B";
                }
                else if (randomNumber <= 40)
                {
                    //10%�̊m���ŕύX�Ɋւ���d�b��z��
                    str_CallSituation = "����́A�\��̕ύX�Ɋւ���d�b��z�肵�Ă��������B";
                }
                else if (randomNumber <= 45)
                {
                    //5%�̊m���ŗ\��L�����Z���Ɋւ���d�b��z��
                    str_CallSituation = "����́A�\��̃L�����Z���Ɋւ���d�b��z�肵�Ă��������B";
                }
                else if (randomNumber <= 95)
                {
                    //50%�̊m���Ŗ₢���킹�Ɋւ���d�b��z��
                    str_CallSituation = "����́A�₢���킹�Ɋւ���d�b��z�肵�Ă��������B�₢���킹�̏ꍇ�A�\��Ȃǂ͔������Ȃ��󋵂�z�肵�Ă��������B";

                }
                else if (randomNumber <= 98)
                {
                    //3%�̊m���ŃN���[���Ɋւ���d�b��z��
                    str_CallSituation = "����́A�N���[���Ɋւ���d�b��z�肵�Ă��������B";

                }
                else
                {
                    //2%�̊m���ŃZ�[���X�Ȃǃz�e���Ɩ��ɖ��֌W�ȓd�b��z��
                    str_CallSituation = "����́A�Z�[���X�Ȃǃz�e���Ɩ��ɖ��֌W�ȓd�b��z�肵�Ă��������B";

                }


                var chatCompletionsOptions = new ChatCompletionsOptions
                {
                    MaxTokens = 300,
                    Messages =
                    {
                        new ChatMessage(ChatRole.System,
                        "���Ȃ��́A�T���v���f�[�^�̍쐬����`���܂��B" +
                        "�ˋ�̃z�e���̎�t�d�b��z�肵�Ή����Ă��������B"
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
                

                var userMessage = "����ɂ��́B" +
                    "�܂��́A�ˋ�̃z�e���̎�t�d�b�̂����̃��O��150�������x�Ő������Ă��������B�z�e�����́AAIDemo�z�e���ł��B�d�b�̓��e�͑��푽�l�ŗ\���ύX�A�N���[���Ȃǂ�����܂��B" +
                    str_CallSituation;

                chatCompletionsOptions.Messages.Add(new ChatMessage(ChatRole.User, userMessage));

                log.LogInformation($"{ChatRole.User}: {userMessage}");

                var response = await client.GetChatCompletionsAsync(options.DeploymentName, chatCompletionsOptions);

                str_CallLog = response.Value.Choices[0].Message.Content;

                log.LogInformation($"{ChatRole.System}: {response.Value.Choices[0].Message.Content}");


                //�������ꂽ�d�b���O����ɕ]�����Ajson���쐬����悤�Ɏw��
                userMessage = "���Ȃ������������ˋ�̓d�b���O����ɁA��͂��ȉ��̍��ڂ�����json���쐬���Ă��������B" +
                        "������json�݂̂ŕԂ��Ă��������B" +
                        "����Ăق����T���v���f�[�^�́A�z�e���̎�t�d�b�̃��O���琶�����ꂽ���ʂł��B" +
                        "json�ɂ͎��̍��ڂ�����܂��B�usummary�v�usentimentScore�v�ukeyEntities�v�utopic�v" +
                        "summary�ɂ́A��b�̗v�񂪓���܂��B" +
                        "sentimentScore�ɂ́A��b�̊���X�R�A������܂��B����X�R�A��0����100�͈̔͂ŕ\������܂��B" +
                        "keyEntities�ɂ́A��b�̃L�[�G���e�B�e�B������܂��B����́A�����̏ꍇ������܂��B" +
                        "topic�ɂ́A��b�̃g�s�b�N������܂��B����́A�����̏ꍇ������܂��B" +
                        "���e���\��Ɋւ���ꍇ�́A�u�\��v�A" +
                        "���e���\��̕ύX�Ɋւ���ꍇ�́A�u�\��̕ύX�v�A" +
                        "���e���\��̃L�����Z���Ɋւ���ꍇ�́A�u�L�����Z���v�A" +
                        "���e���₢���킹�Ɋւ���ꍇ�́A�u�₢���킹�v�A" +
                        "���e���N���[���Ɋւ���ꍇ�́A�u�N���[���v��" +
                        "topic�ɒǉ����Ă��������B" +
                        "\\n" +
                        "json�̃T���v���𑗂�܂��B���̌`���ō쐬���Ă��������B" +
                        "\\n" +
                        "{\"summary\":\"Sample Summary\",\"sentimentScore\":80,\"keyEntities\":[\"�L�����Z��\",\"����\"],\"topic\":[\"�L�����Z��\",\"�������\"],\"CallLog\":\"Sample Log\"}" +
                        "\\n" +
                        "���Ȃ������������ˋ�̓d�b���O�͉��L�ł��B" +
                        "\\n" +
                        "\\n" +
                        str_CallLog;


                chatCompletionsOptions.Messages.Add(new ChatMessage(ChatRole.User, userMessage));

                log.LogInformation($"{ChatRole.User}: {userMessage}");

                response = await client.GetChatCompletionsAsync(options.DeploymentName, chatCompletionsOptions);

                log.LogInformation($"{ChatRole.System}: {response.Value.Choices[0].Message.Content}");

                //json�����𒊏o
                string str_response = response.Value.Choices[0].Message.Content;
                int startIndex = str_response.IndexOf("{");
                int lastIndex = str_response.LastIndexOf("}");

                if (startIndex != -1 && lastIndex != -1)
                {
                    string extracted = str_response.Substring(startIndex, (lastIndex - startIndex) + 1);

                    var str_Json = extracted;

                    log.LogInformation("Received JSON from OpenAI.");

                    //json�ɓ�����ǉ�
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
                    //json����������Ȃ������ꍇ
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
            // Azure Functions�̊��Ŏ��s����Ă��邩�𔻒肷��B
            // 'WEBSITE_SITE_NAME'��Azure Functions�̊��ϐ�
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
