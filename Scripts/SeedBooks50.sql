-- Sample data for Books database: 8 publishers, 50 books (unique ISBN-13).
-- Run against the Books catalog database after migrations.

SET NOCOUNT ON;

DELETE FROM dbo.Books;
DELETE FROM dbo.Publishers;

DBCC CHECKIDENT ('dbo.Books', RESEED, 0);
DBCC CHECKIDENT ('dbo.Publishers', RESEED, 0);

SET IDENTITY_INSERT dbo.Publishers ON;

INSERT INTO dbo.Publishers (PublisherId, Name) VALUES
(1, N'O''Reilly Media'),
(2, N'Pearson Education'),
(3, N'John Wiley & Sons'),
(4, N'MIT Press'),
(5, N'No Starch Press'),
(6, N'Manning Publications'),
(7, N'Apress'),
(8, N'Packt Publishing');

SET IDENTITY_INSERT dbo.Publishers OFF;

DBCC CHECKIDENT ('dbo.Publishers', RESEED, 8);

INSERT INTO dbo.Books (Isbn13, Title, Subtitle, PublicationYear, PublisherId, Pages, LanguageCode, Description, CreatedAt) VALUES
(N'9781000000001', N'Entity Framework Core in Action', N'Modern data access for .NET', 2022, 1, 520, N'en', N'Covers DbContext, migrations, and performance tuning.', SYSUTCDATETIME()),
(N'9781000000002', N'Designing Data-Intensive Applications', N'The big ideas behind reliable systems', 2017, 4, 616, N'en', N'Foundations of distributed systems and storage engines.', SYSUTCDATETIME()),
(N'9781000000003', N'Clean Architecture', N'A Craftsman''s Guide to Software Structure', 2017, 2, 432, N'en', N'Principles for maintainable application design.', SYSUTCDATETIME()),
(N'9781000000004', N'The Pragmatic Programmer', N'Your journey to mastery', 2019, 1, 352, N'en', N'Classic tips for career-long improvement.', SYSUTCDATETIME()),
(N'9781000000005', N'Python Crash Course', N'A hands-on project-based introduction', 2019, 5, 544, N'en', N'Starter-friendly Python with small projects.', SYSUTCDATETIME()),
(N'9781000000006', N'Rust for Rustaceans', N'Idiomatic patterns for experienced programmers', 2021, 4, 304, N'en', N'Advanced Rust: traits, async, and FFI.', SYSUTCDATETIME()),
(N'9781000000007', N'Kubernetes in Action', N'Second edition', 2022, 6, 712, N'en', N'Pods, services, Helm, and production operations.', SYSUTCDATETIME()),
(N'9781000000008', N'Learning SQL', N'Generate, manipulate, and retrieve data', 2020, 2, 352, N'en', N'SQL from SELECTs through window functions.', SYSUTCDATETIME()),
(N'9781000000009', N'Blazor WebAssembly by Example', N'Build SPA apps with C#', 2023, 8, 410, N'en', N'Components, routing, and API integration.', SYSUTCDATETIME()),
(N'9781000000010', N'SQL Server Performance Tuning', N'Practical techniques for DBAs', 2021, 7, 480, N'en', N'Indexing, waits, and query plans.', SYSUTCDATETIME()),
(N'9781000000011', N'JavaScript: The Good Parts', N'Unearthing excellence in a sea of quirks', 2008, 1, 176, N'en', N'A slim guide to effective JS style.', SYSUTCDATETIME()),
(N'9781000000012', N'Patterns of Enterprise Application Architecture', NULL, 2002, 2, 560, N'en', N'Layering, ORM patterns, and web services.', SYSUTCDATETIME()),
(N'9781000000013', N'Artificial Intelligence: A Modern Approach', N'Fourth edition', 2020, 4, 1136, N'en', N'Breadth-first survey of AI techniques.', SYSUTCDATETIME()),
(N'9781000000014', N'Computer Networking', N'A top-down approach', 2021, 2, 864, N'en', N'From application layer down to physical.', SYSUTCDATETIME()),
(N'9781000000015', N'Introduction to Algorithms', N'Third edition', 2009, 4, 1312, N'en', N'Classic algorithms and analysis.', SYSUTCDATETIME()),
(N'9781000000016', N'Database System Concepts', N'Seventh edition', 2019, 2, 1376, N'en', N'Transactions, recovery, and distributed databases.', SYSUTCDATETIME()),
(N'9781000000017', N'Operating System Concepts', N'Tenth edition', 2018, 3, 1008, N'en', N'Processes, memory, filesystems, and security.', SYSUTCDATETIME()),
(N'9781000000018', N'Computer Systems: A Programmer''s Perspective', N'Third edition', 2015, 2, 1120, N'en', N'Bits, assembly, linking, and concurrency.', SYSUTCDATETIME()),
(N'9781000000019', N'Site Reliability Engineering', N'How Google runs production systems', 2016, 1, 552, N'en', N'SLOs, incident response, and capacity planning.', SYSUTCDATETIME()),
(N'9781000000020', N'The DevOps Handbook', N'How to create world-class agility', 2016, 1, 480, N'en', N'Lean, CI/CD, and measurement.', SYSUTCDATETIME()),
(N'9781000000021', N'Infrastructure as Code', N'Managing servers in the cloud', 2020, 1, 384, N'en', N'Terraform, testing, and pipelines.', SYSUTCDATETIME()),
(N'9781000000022', N'Secrets and Lies', N'Digital security in a networked world', 2015, 3, 432, N'en', N'Security mindset and cryptography basics.', SYSUTCDATETIME()),
(N'9781000000023', N'Cryptography Engineering', N'Design principles and practical applications', 2010, 3, 384, N'en', N'Building secure systems end to end.', SYSUTCDATETIME()),
(N'9781000000024', N'Building Microservices', N'Designing fine-grained systems', 2021, 1, 616, N'en', N'Decomposition, boundaries, and logger.', SYSUTCDATETIME()),
(N'9781000000025', N'Monolith to Microservices', N'Evolutionary patterns', 2019, 1, 272, N'en', N'Strangler fig and data synchronization.', SYSUTCDATETIME()),
(N'9781000000026', N'Fundamentals of Software Architecture', N'An engineering approach', 2020, 1, 448, N'en', N'Architecture styles and fitness functions.', SYSUTCDATETIME()),
(N'9781000000027', N'Accelerate', N'The science of lean software delivery', 2018, 4, 288, N'en', N'DORA metrics and high-performing teams.', SYSUTCDATETIME()),
(N'9781000000028', N'The Phoenix Project', N'A novel about IT and DevOps', 2013, 3, 352, N'en', N'Velocity through flow and feedback.', SYSUTCDATETIME()),
(N'9781000000029', N'The Unicorn Project', N'A novel about developers and digital disruption', 2019, 3, 352, N'en', N'Sister story to The Phoenix Project.', SYSUTCDATETIME()),
(N'9781000000030', N'Continuous Delivery', N'Reliable software releases through automation', 2010, 4, 512, N'en', N'Build pipelines and deployment patterns.', SYSUTCDATETIME()),
(N'9781000000031', N'Release It!', N'Design and deploy production-ready software', 2018, 1, 432, N'en', N'Stability patterns and capacity.', SYSUTCDATETIME()),
(N'9781000000032', N'Logger Engineering', N'High-quality systems through thoughtful instrumentation', 2022, 1, 320, N'en', N'Traces, metrics, and SRE practices.', SYSUTCDATETIME()),
(N'9781000000033', N'Learning Domain-Driven Design', N'Aligning software with complexity', 2021, 6, 360, N'en', N'Strategic design and bounded contexts.', SYSUTCDATETIME()),
(N'9781000000034', N'Implementing Domain-Driven Design', NULL, 2014, 6, 688, N'en', N'Vernacular DDD with examples.', SYSUTCDATETIME()),
(N'9781000000035', N'C# in Depth', N'Fourth edition', 2019, 6, 528, N'en', N'Advanced C# language features.', SYSUTCDATETIME()),
(N'9781000000036', N'Concurrency in C# Cookbook', N'Asynchronous, parallel, and multithreaded programming', 2019, 1, 268, N'en', N'async/await patterns and pitfalls.', SYSUTCDATETIME()),
(N'9781000000037', N'ASP.NET Core in Action', N'Third edition', 2023, 6, 704, N'en', N'Middleware, Razor Pages, and minimal APIs.', SYSUTCDATETIME()),
(N'9781000000038', N'Pro ASP.NET Core 6', N'Development fundamentals', 2022, 7, 1408, N'en', N'Comprehensive MVC and Blazor coverage.', SYSUTCDATETIME()),
(N'9781000000039', N'Blazor in Action', N'Build interactive UIs with C#', 2022, 6, 400, N'en', N'Components and SignalR integration.', SYSUTCDATETIME()),
(N'9781000000040', N'Learning Blazor', N'Build single-page apps with WebAssembly and SignalR', 2022, 7, 344, N'en', N'Hands-on Blazor Server and WASM.', SYSUTCDATETIME()),
(N'9781000000041', N'Programming Entity Framework', N'DbContext API', 2011, 2, 912, N'en', N'Earlier EF stack deep dive.', SYSUTCDATETIME()),
(N'9781000000042', N'High Performance MySQL', N'Optimization and replication', 2012, 1, 792, N'en', N'Tuning InnoDB and scaling reads.', SYSUTCDATETIME()),
(N'9781000000043', N'PostgreSQL: Up and Running', N'A practical guide', 2017, 1, 250, N'en', N'Administration and extensions.', SYSUTCDATETIME()),
(N'9781000000044', N'MongoDB: The Definitive Guide', N'Third edition', 2019, 1, 368, N'en', N'Document modeling and aggregation.', SYSUTCDATETIME()),
(N'9781000000045', N'Redis in Action', N'Caching, messaging, and streams', 2022, 6, 384, N'en', N'Patterns for in-memory data structures.', SYSUTCDATETIME()),
(N'9781000000046', N'Kafka: The Definitive Guide', N'Real-time data and stream processing', 2021, 1, 312, N'en', N'Producers, consumers, and exactly-once.', SYSUTCDATETIME()),
(N'9781000000047', N'Streaming Systems', N'The what, where, when, and how of large-scale processing', 2018, 4, 352, N'en', N'Beam model and windowing.', SYSUTCDATETIME()),
(N'9781000000048', N'Data Mesh', N'Delivering data-driven value at scale', 2022, 4, 224, N'en', N'Domain-oriented distributed data ownership.', SYSUTCDATETIME()),
(N'9781000000049', N'Fundamentals of Data Engineering', N'Plan and build robust data systems', 2022, 1, 432, N'en', N'Ingestion, storage, and orchestration.', SYSUTCDATETIME()),
(N'9781000000050', N'Machine Learning Engineering', N'Building production ML systems', 2020, 4, 416, N'en', N'Training, serving, and monitoring models.', SYSUTCDATETIME());

SELECT COUNT(*) AS PublisherCount FROM dbo.Publishers;
SELECT COUNT(*) AS BookCount FROM dbo.Books;
