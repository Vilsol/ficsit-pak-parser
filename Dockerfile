# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:8.0 as build-env

WORKDIR /src

COPY FicsitPakParser.csproj .
RUN dotnet restore

COPY FicsitPakParser.cs .
COPY ParserService.cs .
COPY Protos Protos

RUN dotnet publish -c Release -o /publish


FROM mcr.microsoft.com/dotnet/aspnet:8.0 as runtime

WORKDIR /publish

COPY --from=build-env /publish .
COPY FactoryGame.usmap .

EXPOSE 80

ENTRYPOINT ["./FicsitPakParser"]
CMD ["FactoryGame.usmap", "--urls", "http://0.0.0.0:50051"]