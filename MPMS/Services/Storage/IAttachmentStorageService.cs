using System.IO;
using System.Threading.Tasks;

namespace MPMS.Services.Storage
{
    public interface IAttachmentStorageService
    {
        /// <summary>
        /// Saves a file stream to storage and returns the stored path/URI.
        /// </summary>
        Task<string> SaveFileAsync(string container, string fileName, Stream fileStream);

        /// <summary>
        /// Deletes a file from storage.
        /// </summary>
        Task DeleteFileAsync(string container, string storedFileName);
    }
}
