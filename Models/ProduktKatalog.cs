using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ServiceWorker.DTO;

public class ProduktKatalog
{
[BsonId]
[BsonRepresentation(BsonType.ObjectId)]
public string? ProductId {get; set;}
public string? CategoryCode {get; set;}
public string? ProductName {get; set;}
public string? ProductDescription {get; set;}
public string? itemCondition {get; set;}
}