FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from the project files only, so this layer is cached until a package changes.
COPY Directory.Build.props Directory.Packages.props global.json OrderManagement.slnx ./
COPY src/OrderManagement.Domain/OrderManagement.Domain.csproj src/OrderManagement.Domain/
COPY src/OrderManagement.Application/OrderManagement.Application.csproj src/OrderManagement.Application/
COPY src/OrderManagement.Infrastructure/OrderManagement.Infrastructure.csproj src/OrderManagement.Infrastructure/
COPY src/OrderManagement.Api/OrderManagement.Api.csproj src/OrderManagement.Api/
RUN dotnet restore src/OrderManagement.Api

# database/ is needed because the SQL scripts are embedded into the Infrastructure assembly.
COPY src/ src/
COPY database/ database/
RUN dotnet publish src/OrderManagement.Api -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "OrderManagement.Api.dll"]
