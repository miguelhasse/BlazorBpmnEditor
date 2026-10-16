# Hasseware.AspNetCore.Components.Bpmn.Storage.EfCore (preview)

A BPMN document store over any Entity Framework Core database. Documents live in one table; the version column is the revision.

```csharp
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyBpmnDocuments(pathCollation: "Latin1_General_100_BIN2");   // an ordinal collation where the default is not
}

builder.Services.AddDbContextFactory<AppDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddBpmnEfCoreDocumentStore<AppDbContext>();
```

Add the table with a migration as with any other.

- **Revision.** A number that goes up by one with every write, kept by the store, so it works the same on every provider. It starts from the clock when a document is created, so a document deleted and created again never reuses a revision that an editor may still hold.
- **Atomic, no read-then-write.** An update is one `UPDATE … WHERE Version = @held`; when it changes no row the store tells a missing document from a
  moved one. A create is an insert that the primary key `(Tenant, Path)` refuses when the document exists.
- **Folders are ranges.** Everything under `a/b` is a path between `a/b/` and `a/b0`, so `%` and `_` in a name cannot widen a query. That needs an ordinal
  (binary) comparison of the path column: SQLite and PostgreSQL's `C` collation do it by default; on SQL Server pass `pathCollation: "Latin1_General_100_BIN2"`.
- **Listing** reads only paths, sizes and times and builds folders in memory, so it considers at most `MaxListEntries` (100,000) documents per call; beyond that,
  list a subfolder or give a name prefix.
- **Tenants** are a column of the key, applied by the store, never by the caller.
- Size is recorded beside the content, so a size check never loads it.
- **Changes made outside the store** (a script updating the table) are not seen by editors until they read again; see the ingress samples for
  forwarding a database's own change signal to the bus.
