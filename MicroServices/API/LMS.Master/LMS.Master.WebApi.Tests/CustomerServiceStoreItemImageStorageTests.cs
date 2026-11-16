using AutoMapper;
using LMS.Master.BusinessSerive.Data;
using LMS.Master.BusinessSerive.Services;
using LMS.Master.DTO.Entity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace LMS.Master.WebApi.Tests
{
    public class CustomerServiceStoreItemImageStorageTests : IClassFixture<RazorpaySqlServerFixture>
    {
        private readonly RazorpaySqlServerFixture _fixture;

        public CustomerServiceStoreItemImageStorageTests(RazorpaySqlServerFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task UploadAndRemove_UseContentRootStoragePath_AndCleanupPreviousImageOnReplacement()
        {
            if (!_fixture.IsAvailable)
            {
                return;
            }

            var tenant = $"tenant-image-{Guid.NewGuid():N}@example.com";
            var store = $"S{Guid.NewGuid():N}".Substring(0, 7).ToUpperInvariant();

            await using var context = _fixture.CreateDbContext();
            var service = CreateServiceWithContentRoot(context, out var contentRoot);

            await SeedMastersAndItemAsync(context, tenant, store);

            var item = await context.StoreItemMasters.FirstAsync(x =>
                x.TenantName == tenant
                && x.StoreCode == store
                && x.ServiceType == "Dry Clean"
                && x.Category == "Shirts"
                && x.ItemName == "Test Shirt");

            var first = BuildJpegFormFile("first.jpg");
            var upload1 = await service.UploadStoreItemImage(tenant, store, item.Id, first);
            Assert.Equal(HttpStatusCode.OK, upload1.StatusCode);

            var reloaded = await context.StoreItemMasters.FirstAsync(x => x.Id == item.Id);
            var firstPath = reloaded.ImagePath;
            Assert.False(string.IsNullOrWhiteSpace(firstPath));
            Assert.StartsWith("~/uploads/store-item-images/", firstPath, StringComparison.OrdinalIgnoreCase);

            var root = Path.Combine(contentRoot, "wwwroot", "uploads", "store-item-images");
            var webRoot = Path.Combine(contentRoot, "wwwroot");
            var firstAbsolute = ResolveAbsolutePath(webRoot, firstPath);
            Assert.StartsWith(Path.GetFullPath(root), Path.GetFullPath(firstAbsolute), StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(firstAbsolute));

            var second = BuildJpegFormFile("second.jpg");
            var upload2 = await service.UploadStoreItemImage(tenant, store, item.Id, second);
            Assert.Equal(HttpStatusCode.OK, upload2.StatusCode);

            reloaded = await context.StoreItemMasters.FirstAsync(x => x.Id == item.Id);
            var secondPath = reloaded.ImagePath;
            Assert.NotEqual(firstPath, secondPath);

            var secondAbsolute = ResolveAbsolutePath(webRoot, secondPath);
            Assert.True(File.Exists(secondAbsolute));
            Assert.False(File.Exists(firstAbsolute));

            var remove = await service.RemoveStoreItemImage(tenant, store, item.Id);
            Assert.Equal(HttpStatusCode.OK, remove.StatusCode);

            reloaded = await context.StoreItemMasters.FirstAsync(x => x.Id == item.Id);
            Assert.True(string.IsNullOrWhiteSpace(reloaded.ImagePath));
            Assert.False(File.Exists(secondAbsolute));
        }

        private Customerservice CreateServiceWithContentRoot(MasterDbContext context, out string contentRoot)
        {
            contentRoot = Path.Combine(Path.GetTempPath(), "lms-store-item-image-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(contentRoot);
            Directory.CreateDirectory(Path.Combine(contentRoot, "wwwroot"));

            var mapperConfig = new MapperConfiguration(cfg => { });
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["IntegrationSettings:EncryptionKey"] = _fixture.EncryptionKey,
                    ["StoreItemImages:MaxFileSizeBytes"] = (5 * 1024 * 1024).ToString()
                })
                .Build();

            var host = new TestWebHostEnvironment
            {
                ContentRootPath = contentRoot
            };

            return new Customerservice(
                mapperConfig.CreateMapper(),
                context,
                configuration,
                NullLogger<Customerservice>.Instance,
                host);
        }

        private static IFormFile BuildJpegFormFile(string fileName)
        {
            var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };
            var stream = new MemoryStream(bytes);
            return new FormFile(stream, 0, bytes.Length, "imageFile", fileName)
            {
                Headers = new HeaderDictionary(),
                ContentType = "image/jpeg"
            };
        }

        private static string ResolveAbsolutePath(string root, string relative)
        {
            var normalized = (relative ?? string.Empty).Trim().Replace('\\', '/');
            if (normalized.StartsWith("~/", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(2);
            }
            else if (normalized.StartsWith("/", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(1);
            }

            return Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static async Task SeedMastersAndItemAsync(MasterDbContext context, string tenant, string store)
        {
            var now = DateTime.UtcNow;

            context.StoreServiceMasters.Add(new StoreServiceMasterEntity
            {
                TenantName = tenant,
                StoreCode = store,
                MasterType = "Service",
                Name = "Dry Clean",
                Description = "",
                IsActive = true,
                CreatedDate = now,
                ModifiedDate = now
            });

            context.StoreServiceMasters.Add(new StoreServiceMasterEntity
            {
                TenantName = tenant,
                StoreCode = store,
                MasterType = "Category",
                Name = "Shirts",
                Description = "",
                IsActive = true,
                CreatedDate = now,
                ModifiedDate = now
            });

            context.StoreItemMasters.Add(new StoreItemMasterEntity
            {
                TenantName = tenant,
                StoreCode = store,
                ServiceType = "Dry Clean",
                Category = "Shirts",
                ItemName = "Test Shirt",
                Description = "",
                IsActive = true,
                CreatedDate = now,
                ModifiedDate = now
            });

            await context.SaveChangesAsync();
        }

        private sealed class TestWebHostEnvironment : IWebHostEnvironment
        {
            public string ApplicationName { get; set; }
            public IFileProvider WebRootFileProvider { get; set; }
            public string WebRootPath { get; set; }
            public string EnvironmentName { get; set; }
            public string ContentRootPath { get; set; }
            public IFileProvider ContentRootFileProvider { get; set; }
        }
    }
}
