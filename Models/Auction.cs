using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ServiceWorker.DTO
{
    public class Auction
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        [BsonElement("auctionItem")]
        public ProduktKatalog? AuctionItem { get; set; }

        [BsonElement("startingPrice")]
        public decimal StartingPrice { get; set; }

        [BsonElement("currentBid")]
        public decimal CurrentBid { get; set; }

        [BsonElement("startTime")]
        public DateTime StartTime { get; set; }

        [BsonElement("endTime")]
        public DateTime EndTime { get; set; }


        [BsonElement("User")]
        public UserDTO? User { get; set; }
/*
        [BsonElement("Bid")]
        public BidDTO? Bid { get; set; }
*/
    }
}