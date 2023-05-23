namespace ServiceWorker;



public interface IWorkerRepositoryService
{
    void RecieveBid();
    Task validateBid(string id, decimal bidAmount, string bidderId, DateTime bidTime, string auctionId);
    Task ExecuteAsync(CancellationToken stoppingToken);
}