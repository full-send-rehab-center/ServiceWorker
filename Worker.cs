using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using Newtonsoft.Json;
using MongoDB.Driver;
using System;
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

    public void RecieveBid()
    {
        _logger.LogInformation("Recieving bid at {time}", DateTime.Now);
        var factory = new RabbitMQ.Client.ConnectionFactory() { Uri = new Uri("amqp://guest:guest@localhost:5672/") };
        using var connection = factory.CreateConnection();
        using var channel = connection.CreateModel();
        {
            channel.QueueDeclare(queue: "bidQueue",
                                 durable: false,
                                 exclusive: false,
                                 autoDelete: false,
                                 arguments: null);

            var consumer = new EventingBasicConsumer(channel);
            consumer.Received += async (model, ea) =>
            {
                _logger.LogInformation("Bid received at {time}", DateTime.Now);
                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);

                //Deserialize message
                BidDTO? bid = JsonConvert.DeserializeObject<BidDTO>(message);

                if (bid != null)
                {
                    _logger.LogInformation("Auction {auctionId} recieved bid from {bidderId} at {bidTime} for {bidAmount}", bid.AuctionId, bid.BidderId, bid.BidTime, bid.BidAmount);
                    await validateBid(bid.Id, bid.BidAmount, bid.BidderId, bid.BidTime, bid.AuctionId);
                }
                else
                {
                    _logger.LogInformation("Failed to deserialize bid");
                }
                _logger.LogInformation("Data recieved: {message}", message);
            };

            channel.BasicConsume(queue: "bidQueue",
                                 autoAck: true,
                                 consumer: consumer);

        }
    }

    public async Task validateBid(string id, decimal bidAmount, string bidderId, DateTime bidTime, string auctionId)
    {
        Auction auction = AuctionCollection.Find(a => a.Id == auctionId).FirstOrDefault();

        var client = new MongoClient(_config.GetConnectionString("MongoDB"));
        var database = client.GetDatabase("MongoDB:Database");
        var collection = _database.GetCollection<BidDTO>(_config["MongoDB:BidCollection"]);
        var auctionCollection = _database.GetCollection<Auction>(_config["MongoDB:AuctionCollection"]);

        var auctionById = auctionCollection.Find(a => a.Id == auctionId).FirstOrDefault();
        var bid = new BidDTO(id, bidAmount, bidderId, bidTime, auctionId);

        if (bid.BidAmount > auction.CurrentBid)
        {
            auction.CurrentBid = bid.BidAmount;
            auction.User = new UserDTO();
            auction.User.Id = bid.BidderId;
            auctionCollection.ReplaceOne(a => a.Id == auctionId, auction);
            collection.InsertOne(bid);
            _logger.LogInformation("Bid was accepted");
        }
        else
        {
            _logger.LogInformation("Bid was too low");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            RecieveBid();
            await Task.Delay(1000, stoppingToken);
        }
    }
    

}

