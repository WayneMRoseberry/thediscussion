using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using TheDiscussion.Storage.Postgres;

namespace TheDiscussion.Storage.Tests;

/// <summary>Checks the migrations against the EF Core model. Needs no database, so it always runs.</summary>
public class PostgresMigrationsTests
{
    [Fact]
    public void The_latest_migration_snapshot_matches_the_model()
    {
        var options = new DbContextOptionsBuilder<DiscussionDbContext>();
        PostgresOptions.Configure(options, "Host=localhost;Database=model_check_only");
        using var context = new DiscussionDbContext(options.Options);

        var snapshot = context.GetService<IMigrationsAssembly>().ModelSnapshot;
        Assert.NotNull(snapshot);
        var snapshotModel = context.GetService<IModelRuntimeInitializer>().Initialize(
            ((IMutableModel)snapshot.Model).FinalizeModel(), designTime: true, validationLogger: null);
        var currentModel = context.GetService<IDesignTimeModel>().Model;

        var differences = context.GetService<IMigrationsModelDiffer>()
            .GetDifferences(snapshotModel.GetRelationalModel(), currentModel.GetRelationalModel());

        Assert.True(
            differences.Count == 0,
            "The EF Core model has changes that no migration captures. Add one with: " +
            "dotnet ef migrations add <Name> --project src/TheDiscussion.Storage.Postgres --output-dir Migrations. Differences: " +
            string.Join(", ", differences.Select(operation => operation.GetType().Name)));
    }
}
