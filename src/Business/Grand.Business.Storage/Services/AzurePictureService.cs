﻿using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Grand.Business.Core.Interfaces.Common.Logging;
using Grand.Business.Core.Interfaces.Messages;
using Grand.Business.Core.Interfaces.Storage;
using Grand.Domain.Data;
using Grand.Domain.Media;
using Grand.Infrastructure;
using Grand.Infrastructure.Caching;
using Grand.Infrastructure.Configuration;
using MediatR;

namespace Grand.Business.Storage.Services
{
    /// <summary>
    /// Picture service for Windows Azure
    /// </summary>
    public partial class AzurePictureService : PictureService
    {
        #region Fields

        private static BlobContainerClient container = null;
        private readonly AzureConfig _config;
        private readonly IMimeMappingService _mimeMappingService;

        #endregion

        #region Ctor

        public AzurePictureService(IRepository<Picture> pictureRepository,
            ILogger logger,
            IMediator mediator,
            IWorkContext workContext,
            ICacheBase cacheBase,
            IMediaFileStore mediaFileStore,
            MediaSettings mediaSettings,
            StorageSettings storageSettings,
            AzureConfig config,
            IMimeMappingService mimeMappingService
            )
            : base(pictureRepository,
                logger,
                mediator,
                workContext,
                cacheBase,
                mediaFileStore,
                mediaSettings,
                storageSettings)
        {
            _config = config;
            _mimeMappingService = mimeMappingService;

            if (string.IsNullOrEmpty(_config.AzureBlobStorageConnectionString))
                throw new Exception("Azure connection string for BLOB is not specified");
            if (string.IsNullOrEmpty(_config.AzureBlobStorageContainerName))
                throw new Exception("Azure container name for BLOB is not specified");
            if (string.IsNullOrEmpty(_config.AzureBlobStorageEndPoint))
                throw new Exception("Azure end point for BLOB is not specified");

            container = new BlobContainerClient(_config.AzureBlobStorageConnectionString, _config.AzureBlobStorageContainerName);

        }

        #endregion

        #region Utilities

        /// <summary>
        /// Delete picture thumbs
        /// </summary>
        /// <param name="picture">Picture</param>
        protected override async Task DeletePictureThumbs(Picture picture)
        {
            string filter = string.Format("{0}", picture.Id);
            var blobs = container.GetBlobs(Azure.Storage.Blobs.Models.BlobTraits.All, Azure.Storage.Blobs.Models.BlobStates.All, filter);

            foreach (var blob in blobs)
            {
                await container.DeleteBlobAsync(blob.Name);
            }
        }

        /// <summary>
        /// Get picture (thumb) local path
        /// </summary>
        /// <param name="thumbFileName">Filename</param>
        /// <returns>Local picture thumb path</returns>
        protected override async Task<string> GetThumbPhysicalPath(string thumbFileName)
        {
            var thumbFilePath = $"{_config.AzureBlobStorageEndPoint}{_config.AzureBlobStorageContainerName}/{thumbFileName}";
            var blobClient = container.GetBlobClient(thumbFileName);
            bool exists = await blobClient.ExistsAsync();
            return  exists? thumbFilePath : string.Empty;
        }

        /// <summary>
        /// Get picture (thumb) URL 
        /// </summary>
        /// <param name="thumbFileName">Filename</param>
        /// <param name="storeLocation">Store location URL; null to use determine the current store location automatically</param>
        /// <returns>Local picture thumb path</returns>
        protected override string GetThumbUrl(string thumbFileName, string storeLocation = null)
        {
            var url = _config.AzureBlobStorageEndPoint + _config.AzureBlobStorageContainerName + "/";
            url += thumbFileName;
            return url;
        }

        /// <summary>
        /// Save a value indicating whether some file (thumb) already exists
        /// </summary>
        /// <param name="thumbFileName">Thumb file name</param>
        /// <param name="binary">Picture binary</param>
        protected override Task SaveThumb(string thumbFileName, byte[] binary)
        {
                    
            Stream stream = new MemoryStream(binary);
            container.UploadBlob(thumbFileName, stream);

            //Update content type and other properties 
            string contentType = _mimeMappingService.Map(thumbFileName);
            var blobClient = container.GetBlobClient(thumbFileName);            
            BlobProperties properties = blobClient.GetProperties();
            BlobHttpHeaders blobHttpHeaders = new BlobHttpHeaders {
                // Set the MIME ContentType every time the properties 
                // are updated or the field will be cleared
                ContentType = contentType,
                // Populate remaining headers with 
                // the pre-existing properties
                CacheControl = properties.CacheControl,
                ContentDisposition = properties.ContentDisposition,
                ContentEncoding = properties.ContentEncoding,
                ContentHash = properties.ContentHash
            };
            blobClient.SetHttpHeaders(blobHttpHeaders);            
            return Task.CompletedTask;
        }

        #endregion
    }
}
