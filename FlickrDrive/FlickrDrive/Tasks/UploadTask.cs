﻿using System;
using System.IO;
using FlickrNet;

namespace FlickrDrive.Tasks
{

    public class UploadTask : SynchronizeTask
    {
        private readonly string photoPath;
        public string AlbumId;

        public override bool ContainsSynchronizationOfPhoto => true;

        public UploadTask(string photoPath, string albumTitle)
        {
            this.photoPath = photoPath;
            AlbumTitle = albumTitle;
        }

        public UploadTask(string photoPath, string albumID, string albumTitle)
        {
            this.photoPath = photoPath;
            this.AlbumId = albumID;
            AlbumTitle = albumTitle;
        }
        public override void SynchronizeImplementation(FlickrAlive alive)
        {
            string photoId;
            using (FileStream fs = new FileStream(photoPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var photoName = Path.GetFileNameWithoutExtension(photoPath);
                Action a = new Action(() =>
                {
                    fs.Close();
                });
                alive.CancelActions.Add(a);
                photoId = alive.FlickrInstance.UploadPicture(fs, photoName, photoName, null, null, false, false, false, ContentType.None, SafetyLevel.None, HiddenFromSearch.None);
                alive.CancelActions.Remove(a);
                fs.Close();
            }

            if (string.IsNullOrEmpty(AlbumId))
            {
                throw new Exception("AlbumId is not specified.");
            }
            alive.FlickrInstance.PhotosetsAddPhoto(AlbumId, photoId);
        }


    }
}