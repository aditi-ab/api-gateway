using ApiGateway.Domain;
using ApiGateway.Management;
using ApiGateway.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ApiGateway.IntegrationTests;

public sealed class AuthenticationTests
{
    [Fact]
    public async Task Local_administrator_bootstraps_once_and_validates_password()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        await using var db =
            new GatewayDbContext(new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync(ct);
        var service = new LocalAdministratorService(db, new PasswordHasher<LocalAdministrator>());
        var admin = await service.BootstrapAsync("gateway.admin", "A secure local password 42!", ct);
        Assert.NotEqual("A secure local password 42!", admin.PasswordHash);
        Assert.NotNull(await service.ValidateAsync("GATEWAY.ADMIN", "A secure local password 42!", ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.BootstrapAsync("other.admin", "Another secure password 42!", ct));
        Assert.True(LocalAdministratorService.Principal(admin).IsInRole(ManagementAuth.AdministratorRole));
        var created = await service.CreateAsync("route.reader", "Route Reader", ["Reader"], admin.Username, ct);
        Assert.True(created.User.MustChangePassword);
        Assert.Equal("Route Reader", created.User.DisplayName);
        Assert.NotEmpty(created.TemporaryPassword);
        Assert.True(LocalAdministratorService.Principal(created.User).IsInRole("Reader"));
        await service.UpdateAsync(created.User.Id, created.User.ConcurrencyVersion, "Disabled reader",
            ["Reader", "Publisher"], false, admin.Username, ct);
        Assert.Null(await service.ValidateAsync("route.reader", created.TemporaryPassword, ct));
    }

    [Fact]
    public async Task Updating_only_a_display_name_preserves_existing_sessions()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        await using var db =
            new GatewayDbContext(new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync(ct);
        var service = new LocalAdministratorService(db, new PasswordHasher<LocalAdministrator>());
        var admin = await service.BootstrapAsync("gateway.admin", "A secure local password 42!", ct);
        var principal = LocalAdministratorService.Principal(admin);

        var updated = await service.UpdateAsync(admin.Id, admin.ConcurrencyVersion, "Gateway Administrator",
            [ManagementAuth.AdministratorRole], true, admin.Username, ct);

        Assert.Equal("Gateway Administrator", updated.DisplayName);
        Assert.True(await service.ValidatePrincipalAsync(principal, ct));
    }
}