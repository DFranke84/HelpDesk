// After HelpDeskDbContext factory registration:
var booksCs = builder.Configuration.GetConnectionString("Books");
builder.Services.AddDbContextFactory<BooksDbContext>((sp, options) =>
{
    options.UseSqlServer(booksCs);
}, ServiceLifetime.Scoped);
