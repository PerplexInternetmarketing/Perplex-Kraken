﻿using System;
using System.Collections.Generic;
using System.Linq;
using umbraco.cms.businesslogic.packager;
using Umbraco.Core.Events;
using Umbraco.Core.Models;
using Umbraco.Core.Services;

namespace Kraken
{
    public class UmbracoEvent : Umbraco.Core.ApplicationEventHandler
    {
        UmbracoEventMediaData _umbracoEventMediaData;

        public UmbracoEvent()
        {
            MediaService.Saving += MediaService_Saving;
            MediaService.Saved += MediaService_Saved;
            InstalledPackage.BeforeDelete += InstalledPackage_BeforeDelete;
        }

        void InstalledPackage_BeforeDelete(InstalledPackage sender, EventArgs e)
        {
            if (sender.Data.Name == Constants.UmbracoPackageName)
            {
                Kraken.Uninstall();
            }
        }

        void MediaService_Saving(IMediaService sender, SaveEventArgs<IMedia> e)
        {
            try
            {
                if (Configuration.Settings.Enabled)
                    // We want to know if the media files are going to change.
                    // To do this, store the state of all the IMedia nodes before they get changed
                    _umbracoEventMediaData = new UmbracoEventMediaData(e.SavedEntities);
            }
            catch
            {
                // Swallowing exceptions is bad, but we definitely do not want to disrupt the flow of saving media files to Umbraco,
                // otherwise you might not be able to save files anymore at all.
            }
        }

        void MediaService_Saved(IMediaService sender, SaveEventArgs<IMedia> e)
        {
            try
            {
                if (Configuration.Settings.Enabled)
                    foreach (IMedia im in e.SavedEntities)
                        try
                        {
                            // Detect if the media file has been changed in any way
                            bool hasChanged = _umbracoEventMediaData.HasChanged(im);
                            // Get the current krak-status of the media node
                            var status = Kraken.GetKrakStatus(im);
                            // Determine if we are allowed to krak this media node
                            if (status == EnmIsKrakable.Krakable || // It is krakable
                                (status == EnmIsKrakable.Kraked && hasChanged)) // OR it has been kraked before, but a new file has been uploaded
                            {
                                // Compress the image
                                var result = Kraken.Compress(im);
                                // Did the Kraken API yield a valid result?
                                if (result != null && result.success)
                                    // Save the kraked Image to Umbraco
                                    result.Save(im, null, hasChanged);
                            }
                        }
                        catch (KrakenException kex)
                        {
                            switch (kex.Status)
                            {
                                // In case any of these errors occur, we can no longer proceed
                                case enmStatus.BadRequest:
                                case enmStatus.Unauthorized:
                                case enmStatus.Forbidden:
                                case enmStatus.RequestLimitReached:
                                case enmStatus.UnexpectedError:
                                    return;
                                // The following status messages are OK(-ish)
                                case enmStatus.Ok:
                                case enmStatus.FileTooLarge:
                                case enmStatus.UnsupportedMediaType:
                                case enmStatus.UnprocessableEntity:
                                default:
                                    continue;
                            }
                        }
            }
            catch
            {
                // Swallowing exceptions is bad, but we definitely do not want to disrupt the flow of saving media files to Umbraco,
                // otherwise you might not be able to save files anymore at all.
            }
        }

        #region Internal helper class
        /// <summary>
        /// This class is used to detect changes in media images
        /// </summary>
        class UmbracoEventMediaData
        {
            List<UmbracoEventFile> _eventFiles = new List<UmbracoEventFile>();

            public UmbracoEventMediaData(IEnumerable<IMedia> mediaNodes)
            {
                foreach (var im in mediaNodes)
                    _eventFiles.Add(new UmbracoEventFile(im));
            }

            public bool HasChanged(IMedia im)
            {
                if (im != null)
                {
                    // Keep in mind: new media nodes are always 0 in the "saving" event
                    var uef = _eventFiles.FirstOrDefault(x => x.Id == im.Id);
                    if (uef != null)
                    {
                        // Read height
                        int height = 0, width = 0, size = 0;
                        var propertyValue = im.GetValue(Constants.UmbracoPropertyAliasHeight);
                        if (propertyValue != null)
                            if (propertyValue is int)
                                height = (int)propertyValue;
                            else
                                int.TryParse(propertyValue as String, out height);
                        // Read width
                        propertyValue = im.GetValue(Constants.UmbracoPropertyAliasWidth);
                        if (propertyValue != null)
                            if (propertyValue is int)
                                width = (int)propertyValue;
                            else
                                int.TryParse(propertyValue as String, out width);
                        // Read size
                        propertyValue = im.GetValue(Constants.UmbracoPropertyAliasSize);
                        if (propertyValue != null)
                            if (propertyValue is int)
                                size = (int)propertyValue;
                            else int.TryParse(propertyValue as String, out size);

                        return uef.File != Kraken.GetImage(im) ||
                               uef.Height != height ||
                               uef.Width != width ||
                               uef.Size != size;
                    }
                }
                return false;
            }

            class UmbracoEventFile
            {
                int _height;
                int _width;
                int _size;

                public int Id { get; private set; }
                public string File { get; private set; }
                public int Height { get { return _height; } }
                public int Width { get { return _width; } }
                public int Size { get { return _size; } }

                public UmbracoEventFile(IMedia im)
                {
                    Id = im.Id;
                    File = Kraken.GetImage(im); // Filename 
                    // Read height
                    var propertyValue = im.GetValue(Constants.UmbracoPropertyAliasHeight);
                    if (propertyValue != null)
                        if (propertyValue is int)
                            _height = (int)propertyValue;
                        else
                            int.TryParse(propertyValue as String, out _height);
                    // Read width
                    propertyValue = im.GetValue(Constants.UmbracoPropertyAliasWidth);
                    if (propertyValue != null)
                        if (propertyValue is int)
                            _width = (int)propertyValue;
                        else
                            int.TryParse(propertyValue as String, out _width);
                    // Read size
                    propertyValue = im.GetValue(Constants.UmbracoPropertyAliasSize);
                    if (propertyValue != null)
                        if (propertyValue is int)
                            _size = (int)propertyValue;
                        else int.TryParse(propertyValue as String, out _size);
                }
            }
        }
        #endregion
    }
}