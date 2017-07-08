﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using FlickrDrive.Properties;
using FlickrDrive.Tasks;
using FlickrNet;

namespace FlickrDrive
{
    public class Alive : BaseNotifyObject
    {
        public List<Action> CancelActions;
        public FlickrData FlickrData;
        public Flickr FlickrInstance;

        public string Root
        {
            get { return _root; }
            set
            {
                if (_root != null && _root != value)
                {
                    //needs update path before updating meta
                    _root = value;
                    Settings.Default.RootPath = value;
                    Settings.Default.Save();
                    UpdateMeta();
                }
                _root = value;
                OnPropertyChanged();
            }
        }

        public bool IsLoggedIn
        {
            get { return !string.IsNullOrEmpty(Settings.Default.oauthToken)&& !string.IsNullOrEmpty(Settings.Default.oauthTokenSecret); }
        }
        public bool IsSynchronizing
        {
            get { return _isSynchronizing; }
            set
            {
                _isSynchronizing = value;
                OnPropertyChanged();
            }
        }

        public string UserName
        {
            get { return Settings.Default.Username; }
        }

        private BasicSecurity _basicSecurity;
        private bool _isSynchronizing;
        private ObservableCollection<SynchroSet> _allSynchroSets;
        private string _root;

        public ObservableCollection<SynchroSet> AllSynchroSets
        {
            get { return _allSynchroSets; }
            set { _allSynchroSets = value; }
        }

        private List<ISynchronizeTask> SynchronizationTasks { get; set; }

        public int SynchronizationTasksCount
        {
            get { return SynchronizationTasks.Count; }
        }

        public int SynchronizationTasksDoneCount
        {
            get { return SynchronizationTasks.Where(t => t.IsDone).Count(); }
        }

        public string SynchronizationProgressString
        {
            get { return $"Photos synchronized {SynchronizationTasksDoneCount} from {SynchronizationTasksCount}."; }
        }

        public string TasksString
        {
            get { return $"Photos to be synchronized {SynchronizationTasksCount}."; }
        }

        public void Stop()
        {
            SynchronizationTasks.Clear();
            foreach (var cancelAction in CancelActions)
            {
                cancelAction();
            }
            UpdateMeta();
            IsSynchronizing = false;

        }

        public Alive()
        {
            CancelActions = new List<Action>();
            _basicSecurity = new BasicSecurity();
            FlickrInstance = new Flickr(Constants.KEY, Constants.SECRET);
            FlickrData = new FlickrData();
            AllSynchroSets = new ObservableCollection<SynchroSet>();
            SynchronizationTasks = new List<ISynchronizeTask>();
        }

        public void Initialize()
        {
            var synchronizePath = Settings.Default.RootPath;
            if (string.IsNullOrEmpty(synchronizePath))
            {
                synchronizePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) +
                                  Constants.DelimiterInWindowsPath + Constants.ProgramName;
            }

            Root = synchronizePath;
            EnsureDirectoryExist(Root);

            if (string.IsNullOrEmpty(Settings.Default.oauthTokenSecret))
            {
                Authorize();
            }
            GetAccessToken();

            Task.Run(() =>
            {
                UpdateMeta();
            });
        }

        public async void Authorize()
        {
            var requestToken = FlickrInstance.OAuthGetRequestToken(Constants.LOCAL_HOST_ADDRESS);
            var authorizationUrl = FlickrInstance.OAuthCalculateAuthorizationUrl(requestToken.Token, AuthLevel.Delete);
            var p = Process.Start(authorizationUrl);
            await ListenForToken(requestToken);
            OnPropertyChanged(nameof(IsLoggedIn));
        }

        private async Task ListenForToken(OAuthRequestToken requestToken)
        {
            HttpListener listener = new HttpListener();
            listener.Prefixes.Add(Constants.LOCAL_HOST_ADDRESS);
            listener.Start();
            string oauthToken = "";
            string oauthVerifier = "";
            while (true)
            {
                var context = listener.GetContext();
                oauthToken = context.Request.QueryString.Get("oauth_token");
                oauthVerifier = context.Request.QueryString.Get("oauth_verifier");

                Debug.WriteLine(oauthToken, oauthVerifier);
                if (!string.IsNullOrEmpty(oauthToken) && !string.IsNullOrEmpty(oauthVerifier))
                {
                    //form response
                    var outputStream = context.Response.OutputStream;
                    string responseString = "<html><body><h2>Access granted</h2></body></html>";
                    var bytes = Encoding.UTF8.GetBytes(responseString);
                    context.Response.ContentLength64 = bytes.Length;
                    outputStream.Write(bytes, 0, bytes.Length);
                    outputStream.Close();
                    break;
                }

                
            }

            var access = FlickrInstance.OAuthGetAccessToken(requestToken, oauthVerifier);
            Settings.Default.Username = access.Username;
            SaveLoginToken(access.Token, access.TokenSecret);
            return;
        }

        private void GetAccessToken()
        {
            var oAuthToken = _basicSecurity.DecryptText(Settings.Default.oauthToken,
                Constants.EncryptionKey);
            var oAuthAccessTokenSecret = _basicSecurity.DecryptText(Settings.Default.oauthTokenSecret,
                Constants.EncryptionKey);

            FlickrInstance.OAuthAccessToken = oAuthToken;
            FlickrInstance.OAuthAccessTokenSecret = oAuthAccessTokenSecret;
        }

        private void SaveLoginToken(string token, string tokenSecret)
        {

            var enVerif = _basicSecurity.EncryptText(tokenSecret, Constants.EncryptionKey);
            Settings.Default.oauthTokenSecret = enVerif;

            var enToken = _basicSecurity.EncryptText(token, Constants.EncryptionKey);
            Settings.Default.oauthToken = enToken;

            Settings.Default.Save();
        }

        public void Synchronize()
        {
            Task.Run(() =>
            {
                IsSynchronizing = true;
                foreach (var synchronizationTask in SynchronizationTasks)
                {
                    OnPropertyChanged(nameof(SynchronizationTasksDoneCount));
                    OnPropertyChanged(nameof(SynchronizationProgressString));
                    try
                    {
                        synchronizationTask.Synchronize(this);
                    }
                    catch (Exception)
                    {
                        
                    }
                }
                IsSynchronizing = false;
                SynchronizationTasks.Clear();
                UpdateMeta();
            });

        }

        public void UpdateMeta()
        {
            var newSynchroSet = new List<SynchroSet>();
            FlickrData.Sets = FlickrInstance.PhotosetsGetList();
            EnsureDirectoryExist(Root);

            foreach (var set in FlickrData.Sets)
            {
                var setx = new SynchroSet(set.Title, this);
                var testedDirectory = Root + Constants.DelimiterInWindowsPath + set.Title;
                if (!Directory.Exists(testedDirectory))
                {
                    setx.Down = set.NumberOfPhotos;
                }
                else
                {
                    var files = Directory.GetFiles(testedDirectory).FilterPhotos().Select(Path.GetFileNameWithoutExtension);
                    var photos = FlickrInstance.PhotosetsGetPhotos(set.PhotosetId).Select(p => p.Title);
                    foreach (var file in files)
                    {
                        if (!photos.Contains(file))
                        {
                            setx.Up++;
                        }
                    }

                    foreach (var photo in photos)
                    {
                        if (!files.Contains(photo))
                        {
                            setx.Down++;
                        }
                    }
                }
                newSynchroSet.Add(setx);
            }

            var Directories = Directory.GetDirectories(Root);
            foreach (var directory in Directories)
            {
                var directoryName = Path.GetFileName(directory);

                if (FlickrData.Sets.FirstOrDefault(s => s.Title == directoryName) == null)
                {
                    var setx = new SynchroSet(directoryName, this);
                    setx.Up = Directory.GetFiles(directory).FilterPhotos().Count();
                    newSynchroSet.Add(setx);
                }
            }
            AllSynchroSets = new ObservableCollection<SynchroSet>(newSynchroSet);
            OnPropertyChanged(nameof(AllSynchroSets));
            OnPropertyChanged(nameof(TasksString));

        }
        public void AddSynchronizeSet(SynchroSet synchroSet)
        {
            string directory = Root + Constants.DelimiterInWindowsPath + synchroSet.Title;
            EnsureDirectoryExist(directory);

            var set = FlickrData.Sets.FirstOrDefault(i => i.Title == synchroSet.Title);
            if (set == null)
            {
                //create new album, only if there is at least one photo
                if (Directory.GetFiles(directory).FilterPhotos().Any())
                {
                    SynchronizeNewDirectoryUp(directory);
                }
            }
            else
            {
                SynchronizeExistingDirectory(set, directory);
            }   

            OnPropertyChanged(nameof(SynchronizationTasksCount)); 
            OnPropertyChanged(nameof(TasksString));

        }

        private void SynchronizeExistingDirectory(Photoset set, string directory)
        {
            var existingPhotos = FlickrInstance.PhotosetsGetPhotos(set.PhotosetId);
            var localFiles = Directory.GetFiles(directory).FilterPhotos();
            foreach (var file in localFiles)
            {
                if (existingPhotos.FirstOrDefault(p => p.Title == Path.GetFileNameWithoutExtension(file)) == null)
                {
                    var task = new UploadTask(file, set.PhotosetId, set.Title);
                    SynchronizationTasks.Add(task);
                }
            }
            foreach (var photo in existingPhotos)
            {
                if (localFiles.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f) == photo.Title) == null)
                {
                    var task = new DownloadTask(photo.PhotoId,directory,set.Title);
                    SynchronizationTasks.Add(task);
                }
            }
        }

        private void SynchronizeNewDirectoryUp(string directory)
        {
            var files = Directory.GetFiles(directory).FilterPhotos();
            var albumTitle = Path.GetFileName(directory);
            var createAlbumTask = new CreateAlbumTask(files.First(), albumTitle);
            List<UploadTask> allFilesUp = new List<UploadTask>();
            foreach (var file in files)
            {
                if (file == files.First())
                {
                    continue;
                }
                allFilesUp.Add(new UploadTask(file, albumTitle));
            }
            createAlbumTask.PostAction = new Action(() =>
            {
                foreach (var file in allFilesUp)
                {
                    file.AlbumId = createAlbumTask.AlbumId;
                }
            });

            SynchronizationTasks.Add(createAlbumTask);
            SynchronizationTasks.AddRange(allFilesUp);
        }


        private void EnsureDirectoryExist(string directory)
        {
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        public void RemoveSynchronizationOfSet(SynchroSet synchroSet)
        {
            SynchronizationTasks.RemoveAll(t => t.AlbumTitle == synchroSet.Title);            
            OnPropertyChanged(nameof(SynchronizationTasksCount));
            OnPropertyChanged(nameof(TasksString));


        }

        public void Logout()
        {
            Settings.Default.oauthToken = String.Empty;
            Settings.Default.oauthTokenSecret = String.Empty;
            Settings.Default.Username = String.Empty;
            FlickrInstance.OAuthAccessToken = String.Empty;
            FlickrInstance.OAuthAccessTokenSecret = String.Empty;
            OnPropertyChanged(nameof(IsLoggedIn));
            Settings.Default.Save();
        }
    }

    public static class Extensions
    {
        public static IEnumerable<string> FilterPhotos(this string[] str)
        {
            foreach (var s in str)
            {
                var extension = Path.GetExtension(s)?.Substring(1).ToLowerInvariant();
                if (extension == "png" || extension == "jpg" || extension == "gif")
                {
                    yield return s;
                }
            }
        }

    }
}