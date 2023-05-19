using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ServiceWorker.DTO;

public class BidDTO
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; }

    public decimal BidAmount { get; set; }

    public string BidderId { get; set; }
    
    public DateTime BidTime { get; set; }

    public string AuctionId { get; set; }

    public BidDTO(string id, decimal bidAmount, string bidderId, DateTime bidTime, string auctionId)
    {
        Id = id;
        BidAmount = bidAmount;
        BidderId = bidderId;
        BidTime = bidTime;
        AuctionId = auctionId;
    }

   
}