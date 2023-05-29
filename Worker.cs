using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using Newtonsoft.Json;
using MongoDB.Driver;
using System.Threading;
using MongoDB.Bson;
using ServiceWorker.DTO;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace ServiceWorker;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly string? _docPath;
    private readonly string? _rabbitMQ;
    private readonly IMongoDatabase _database;
    private IConfiguration _config;
    private IMongoDatabase Database { get; }
    private IMongoCollection<Auction> AuctionCollection;
    private IMongoCollection<BidDTO> BidCollection;
    private IMongoCollection<UserDTO> UsersCollection;

    public Worker(ILogger<Worker> logger, IConfiguration config)
    {
        //Takes enviroment variable and sets it to the logger
        _logger = logger;
        _docPath = config["DocPath"];
        _rabbitMQ = config["RabbitMQ"];
        _config = config;

        //Retrieves host name and IP address from the current enviroment
        var hostName = System.Net.Dns.GetHostName();
        var ips = System.Net.Dns.GetHostAddresses(hostName);
        var _ipaddress = ips.First().MapToIPv4().ToString();


        //Connects to the database
        MongoClient client = new MongoClient("mongodb+srv://mikkelbojstrup:aha64jmj@auktionshus.67fs0yo.mongodb.net/");
        _database = client.GetDatabase("Auction");
        AuctionCollection = _database.GetCollection<Auction>("AuctionCollection");
        UsersCollection = _database.GetCollection<UserDTO>("UsersCollection");
        BidCollection = _database.GetCollection<BidDTO>("BidCollection");

        //Logs the enviroment variable
        _logger.LogInformation($"File path is set to : {_docPath}");
        _logger.LogInformation($"RabbitMQ connection is set to : {_rabbitMQ}");
        _logger.LogInformation($"MongoDB er sat til: {_config.GetConnectionString("MongoDB")}");

    }

    //var factory = new RabbitMQ.Client.ConnectionFactory() { Uri = new Uri("amqp://guest:guest@localhost:5672/") };

    public void RecieveBid()
{
    var factory = new RabbitMQ.Client.ConnectionFactory() { Uri = new Uri("amqp://guest:guest@localhost:5672/") };
    factory.DispatchConsumersAsync = true;

    using var connection = factory.CreateConnection();
    using var channel = connection.CreateModel();
    {
        channel.QueueDeclare(queue: "bidQueue",
                             durable: false,
                             exclusive: false,
                             autoDelete: false,
                             arguments: null);
        
        channel.QueueBind(queue: "bidQueue",
                          exchange: "bidExchange",
                          routingKey: "bid");

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.Received += async (model, ea) =>
        {
            try
            {
                _logger.LogInformation("Bid received at {time}", DateTime.Now);

                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);

                // Deserialize message
                BidDTO? bid = JsonConvert.DeserializeObject<BidDTO>(message);

                if (bid != null)
                {
                    _logger.LogInformation("Auction {auctionId} received bid from {bidderId} at {bidTime} for {bidAmount}", bid.AuctionId, bid.BidderId, bid.BidTime, bid.BidAmount);

                    await validateBid(bid.Id, bid.BidAmount, bid.BidderId, bid.BidTime, bid.AuctionId);
                    _logger.LogInformation("Bid was validated and processed");
                }
                else
                {
                    _logger.LogInformation("Failed to deserialize bid");
                }
                _logger.LogInformation("Data received: {message}", message);

                // Acknowledge the message
                channel.BasicAck(ea.DeliveryTag, false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error receiving bid");
            }
        };

        channel.BasicConsume(queue: "bidQueue",
                             autoAck: false,
                             consumer: consumer);

        // Wait for the consumer to finish processing messages
        while (consumer.IsRunning)
        {
            Thread.Sleep(100);
        }
    }
}


    public async Task validateBid(string id, decimal bidAmount, string bidderId, DateTime bidTime, string auctionId)
{
    var filter = Builders<Auction>.Filter.Eq(a => a.Id, auctionId);
    var update = Builders<Auction>.Update
        .Set(a => a.CurrentBid, bidAmount)
        .Set(a => a.User, new UserDTO { Id = bidderId });

    var result = await AuctionCollection.UpdateOneAsync(filter, update);

    if (result.ModifiedCount > 0)
    {
        var bid = new BidDTO(id, bidAmount, bidderId, bidTime, auctionId);
        BidCollection.InsertOne(bid);
        _logger.LogInformation("Bid was accepted");
    }
    else
    {
        _logger.LogInformation("Bid was too low");
    }
}


    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
        RecieveBid();

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }
    }


}

