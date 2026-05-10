using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace SoundCloudClient
{
    public class Playlist
    {
        public string Name { get; set; } = "";
        public List<Track> Tracks { get; set; } = new();
    }

    public class LibraryData
    {
        public List<Track> Likes { get; set; } = new();
        public List<Playlist> Playlists { get; set; } = new();
    }

    public class LocalLibrary
    {
        private readonly string _dataFile;

        public LocalLibrary()
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MusicBox");
            Directory.CreateDirectory(folder);
            _dataFile = Path.Combine(folder, "library.json");
        }

        private LibraryData Load()
        {
            if (!File.Exists(_dataFile))
                return new LibraryData();
            var json = File.ReadAllText(_dataFile);
            return JsonConvert.DeserializeObject<LibraryData>(json) ?? new LibraryData();
        }

        private void Save(LibraryData data)
        {
            var json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(_dataFile, json);
        }

        public List<Track> GetLikes()
        {
            return Load().Likes;
        }

        public void AddLike(Track track)
        {
            var data = Load();
            if (data.Likes.Exists(t => t.VideoId == track.VideoId))
                return;
            data.Likes.Insert(0, track);
            Save(data);
        }

        public void RemoveLike(string videoId)
        {
            var data = Load();
            data.Likes.RemoveAll(t => t.VideoId == videoId);
            Save(data);
        }

        public bool IsLiked(string videoId)
        {
            return Load().Likes.Exists(t => t.VideoId == videoId);
        }

        public List<Playlist> GetPlaylists()
        {
            return Load().Playlists;
        }

        public void CreatePlaylist(string name)
        {
            var data = Load();
            if (data.Playlists.Exists(p => p.Name == name))
                return;
            data.Playlists.Add(new Playlist { Name = name });
            Save(data);
        }

        public void DeletePlaylist(string name)
        {
            var data = Load();
            data.Playlists.RemoveAll(p => p.Name == name);
            Save(data);
        }

        public void AddToPlaylist(string playlistName, Track track)
        {
            var data = Load();
            var playlist = data.Playlists.Find(p => p.Name == playlistName);
            if (playlist == null) return;
            if (playlist.Tracks.Exists(t => t.VideoId == track.VideoId))
                return;
            playlist.Tracks.Add(track);
            Save(data);
        }

        public void RemoveFromPlaylist(string playlistName, string videoId)
        {
            var data = Load();
            var playlist = data.Playlists.Find(p => p.Name == playlistName);
            if (playlist == null) return;
            playlist.Tracks.RemoveAll(t => t.VideoId == videoId);
            Save(data);
        }
    }
}
