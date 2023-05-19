using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using Newtonsoft.Json;
using MongoDB.Driver;
using System;
using MongoDB.Bson;
using ServiceWorker.DTO;

namespace ServiceWorker;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly string? _docPath;
    private readonly string? _rabbitMQ;
    private IConfiguration _config;
    private IMongoDatabase _database;
    private IMongoCollection<Auction> AuctionCollection { get; }
    private IMongoCollection<BidDTO> BidCollection { get; }
    private IMongoCollection<UserDTO> UsersCollection { get; }

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

        //Logs the enviroment variable
        _logger.LogInformation($"File path is set to : {_docPath}");
        _logger.LogInformation($"RabbitMQ connection is set to : {_rabbitMQ}");


        //Connects to the database
        var client = new MongoClient(_config["MongoDB:ConnectionString"]);
        _database = client.GetDatabase(_config["MongoDB:Database"]);
        AuctionCollection = _database.GetCollection<Auction>(_config["MongoDB:AuctionCollection"]);
        UsersCollection = _database.GetCollection<UserDTO>(_config["MongoDB:UsersCollection"]);
        BidCollection = _database.GetCollection<BidDTO>(_config["MongoDB:BidCollection"]);

        _logger.LogInformation($"Fil sti er sat til: {_docPath}");
        _logger.LogInformation($"RabbitMQ er sat til: {_rabbitMQ}");
    }

    public void RecieveBid()
    {
        _logger.LogInformation("Recieving bid at {time}", DateTime.Now);
        var factory = new ConnectionFactory() { HostName = _rabbitMQ };
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

        var client = new MongoClient("MongoDB:ConnectionString");
        var database = client.GetDatabase("MongoDB:Database");
        var collection = database.GetCollection<BidDTO>("BidCollection");
        var auctionCollection = database.GetCollection<Auction>("AuctionCollection");
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

