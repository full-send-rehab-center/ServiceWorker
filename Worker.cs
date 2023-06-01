using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using Newtonsoft.Json;
using MongoDB.Driver;
using System.Threading;
using System.Threading.Tasks;
using ServiceWorker.DTO;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace ServiceWorker
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IMongoDatabase _database;
        private IConfiguration _config;
        private IMongoDatabase Database { get; }
        private IMongoCollection<Auction> AuctionCollection;
        private IMongoCollection<BidDTO> BidCollection;
        private IMongoCollection<UserDTO> UsersCollection;
        private IConnection _connection;
        private IModel _channel;
        private readonly string _rabbitMQ;

        public Worker(ILogger<Worker> logger, IConfiguration config)
        {
            // Initialiserer logger og læser konfigurationsoplysninger
            _logger = logger;
            _config = config;

            // Tildeler værdier fra appsettings.json til private variabler
            _rabbitMQ = config["RabbitMQ"];

            // Opretter forbindelse til MongoDB og initialiserer MongoDB-samlinger
            var hostName = System.Net.Dns.GetHostName();
            var ips = System.Net.Dns.GetHostAddresses(hostName);
            var _ipaddress = ips.First().MapToIPv4().ToString();

            // var mongoDBConnectionString = _config["MongoDB:ConnectionString"];
            var mongoDBConnectionString = config["ConnectionString"];

            MongoClient client = new MongoClient(mongoDBConnectionString);
            _database = client.GetDatabase("DatabaseName");
            AuctionCollection = _database.GetCollection<Auction>("CollectionName");
            UsersCollection = _database.GetCollection<UserDTO>("UsersCollection");
            BidCollection = _database.GetCollection<BidDTO>("BidCollection");

            // Logger konfigurationsoplysningerne
         
            _logger.LogInformation($"RabbitMQ connection is set to: {_rabbitMQ}");
            _logger.LogInformation($"MongoDB er sat til: {mongoDBConnectionString}");
            
        }

        public void ConnectRabbitMQ()
        {
            // Opretter en forbindelse og en kanal til RabbitMQ
            // var factory = new ConnectionFactory() { HostName = _rabbitMQ };
            var factory = new RabbitMQ.Client.ConnectionFactory() { Uri = new Uri(_rabbitMQ) };
            // var factory = new RabbitMQ.Client.ConnectionFactory() { Uri = new Uri(rabbitMQConnectionString) };
            factory.DispatchConsumersAsync = true;
            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();
            
            _channel.ExchangeDelete("bidExchange"); // Fjerner den eksisterende udveksling
            _channel.ExchangeDeclare(exchange: "bidExchange", type: ExchangeType.Topic, durable: false);


            // Erklærer en kø og binder den til en udveksling med en rute
            _channel.QueueDeclare(queue: "bidQueue", durable: false, exclusive: false, autoDelete: false, arguments: null);
            _channel.QueueBind(queue: "bidQueue", exchange: "bidExchange", routingKey: "bid");

            // Opretter en forbruger, der lytter på meddelelser i køen
            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.Received += async (model, ea) =>
            {
                try
                {
                    _logger.LogInformation("Bid received at {time}", DateTime.Now);

                    // Modtager og deserialiserer budmeddelelsen
                    var body = ea.Body.ToArray();
                    var message = Encoding.UTF8.GetString(body);
                    BidDTO? bid = JsonConvert.DeserializeObject<BidDTO>(message);

                    if (bid != null)
                    {
                        _logger.LogInformation("Auction {auctionId} received bid from {bidderId} at {bidTime} for {bidAmount}", bid.AuctionId, bid.BidderId, bid.BidTime, bid.BidAmount);

                        // Validerer og behandler budet
                        await ValidateBid(bid.Id, bid.BidAmount, bid.BidderId, bid.BidTime, bid.AuctionId);
                        _logger.LogInformation("Bid was validated and processed");
                    }
                    else
                    {
                        _logger.LogInformation("Failed to deserialize bid");
                    }

                    _logger.LogInformation("Data received: {message}", message);

                    // Bekræfter modtagelsen af budmeddelelsen
                    _channel.BasicAck(ea.DeliveryTag, false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error receiving bid");
                }
            };

            // Begynder at forbruge budmeddelelser fra køen
            _channel.BasicConsume(queue: "bidQueue", autoAck: false, consumer: consumer);
        }

        public async Task ValidateBid(string id, decimal bidAmount, string bidderId, DateTime bidTime, string auctionId)
        {
            // Finder auktionsdokumentet
            var auction = await AuctionCollection.Find(a => a.Id == auctionId).FirstOrDefaultAsync();

            if (auction == null)
            {
                _logger.LogInformation("Auction not found");
                return;
            }

            if (bidAmount <= auction.CurrentBid)
            {
                // Budet er for lavt
                _logger.LogInformation("Bid was too low");
                return;
            }

            // Opdaterer auktionsdokumentet med det nye bud
            var filter = Builders<Auction>.Filter.Eq(a => a.Id, auctionId);
            var update = Builders<Auction>.Update
                .Set(a => a.CurrentBid, bidAmount)
                .Set(a => a.User, new UserDTO { Id = bidderId });

            var result = await AuctionCollection.UpdateOneAsync(filter, update);

            if (result.IsAcknowledged && result.ModifiedCount > 0)
            {
                // Indsætter budet i BidCollection
                var bid = new BidDTO(id, bidAmount, bidderId, bidTime, auctionId);
                BidCollection.InsertOne(bid);
                _logger.LogInformation("Bid was accepted");
            }
            else
            {
                _logger.LogInformation("Bid was rejected");
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);

            // Opretter forbindelse til RabbitMQ og begynder at lytte efter budmeddelelser
            ConnectRabbitMQ();

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            // Afslutter forbindelsen og kanalen til RabbitMQ ved afslutning af tjenesten
            _channel?.Dispose();
            _connection?.Dispose();

            await base.StopAsync(cancellationToken);
        }
    }
}


