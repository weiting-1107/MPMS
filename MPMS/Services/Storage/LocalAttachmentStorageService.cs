using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;

namespace MPMS.Services.Storage
{
    public class LocalAttachmentStorageService : IAttachmentStorageService
    {
        private readonly IWebHostEnvironment _env;

        public LocalAttachmentStorageService(IWebHostEnvironment env)
        {
            _env = env;
        }

        public async Task<string> SaveFileAsync(string container, string fileName, Stream fileStream)
        {
            // Define local save directory: wwwroot/uploads/{container}/
            var uploadDir = Path.Combine(_env.WebRootPath, "uploads", container);
            if (!Directory.Exists(uploadDir))
            {
                Directory.CreateDirectory(uploadDir);
            }

            var fileExtension = Path.GetExtension(fileName);
            var uniqueFileName = $"{Guid.NewGuid()}{fileExtension}";
            var physicalPath = Path.Combine(uploadDir, uniqueFileName);

            using (var destinationStream = new FileStream(physicalPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await fileStream.CopyToAsync(destinationStream);
            }

            // Return relative path for web access, e.g. /uploads/{container}/{uniqueFileName}
            return $"/uploads/{container}/{uniqueFileName}";
        }

        public Task DeleteFileAsync(string container, string storedFileName)
        {
            var physicalPath = Path.Combine(_env.WebRootPath, "uploads", container, storedFileName);
            if (File.Exists(physicalPath))
            {
                File.Delete(physicalPath);
            }
            return Task.CompletedTask;
        }
    }
}
