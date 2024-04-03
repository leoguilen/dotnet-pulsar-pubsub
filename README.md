# Pub/Sub Apache Pulsar 📨
Demonstrates how to use the Apache Pulsar Pub/Sub messaging service using the [C# client library](https://pulsar.apache.org/docs/3.2.x/client-libraries-dotnet/).

## How to run
1. Clone this repository
2. Execute the following script to up the Apache P Pulsar service using Docker Compose:
```bash
chmod +x ./scripts/deploy-local-environment.sh 
./scripts/deploy-local-environment.sh
```
3. Run the `Producer` project:
```bash
dotnet run --project src/Producer
```
4. Run the `Consumer` project:
```bash
dotnet run --project src/Consumer
```
5. Make a request to the `Producer API` project to send a message to the Pulsar broker:
```bash
curl -X POST "http://<host>/api/messages" -H "Content-Type: application/json" -d '{"content": "Hello, Pulsar!"}'
```

## Resources
- [Apache Pulsar](https://pulsar.apache.org/)
- [DotPulsar](https://github.com/apache/pulsar-dotpulsar/wiki)
- [Dotnet 8.0](https://dotnet.microsoft.com/download/dotnet/8.0)