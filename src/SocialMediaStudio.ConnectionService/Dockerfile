FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source
COPY src/SocialMediaStudio.ConnectionService/ src/SocialMediaStudio.ConnectionService/
COPY src/SocialMediaStudio/Services/ src/SocialMediaStudio/Services/
RUN dotnet publish src/SocialMediaStudio.ConnectionService/SocialMediaStudio.ConnectionService.csproj -c Release -o /out
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /out/ ./
ENV ASPNETCORE_HTTP_PORTS=8080
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "SocialMediaStudio.ConnectionService.dll"]
