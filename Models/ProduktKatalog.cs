using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ServiceWorker.DTO;

public class ProduktKatalog
{
[BsonId]
[BsonRepresentation(BsonType.ObjectId)]
public string? CategoryId {get; set;}
public string? CategoryCode {get; set;}
public string? CategoryName {get; set;}
public string? CategoryDescription {get; set;}
}