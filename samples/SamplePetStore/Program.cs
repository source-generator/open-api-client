using SamplePetStore.Clients;

using var httpClient = new HttpClient();
httpClient.BaseAddress = new Uri("https://petstore3.swagger.io/api/v3/");

PetStoreClient petStoreClient = new(httpClient);
StoreClient storeClient = new(httpClient);

Console.WriteLine($"Generated client available: {petStoreClient.GetType().FullName}");
Console.WriteLine($"Generated client available: {storeClient.GetType().FullName}");
