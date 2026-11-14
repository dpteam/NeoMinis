using System;
using System.Collections.Generic;
using Social.Core;

namespace Social.Web
{
    /// <summary>
    /// Photo albums: named buckets of photo posts. An album never owns a file, so
    /// moving a picture is one field on the post and deleting an album leaves the
    /// photos on the wall.
    /// </summary>
    public sealed partial class ApiRouter
    {
        private string AlbumJson(Album a, User viewer)
        {
            JsonBuilder b = new JsonBuilder(512);
            b.BeginObject();
            b.Prop("id", a.Id);
            b.Prop("name", a.Name);
            b.Prop("description", a.Description);
            b.Prop("cover", _store.AlbumCover(a));
            b.Prop("photos", _store.CountAlbumPhotos(a.Id));
            b.Prop("created", Util.Iso(a.Created));
            b.Prop("updated", Util.Iso(a.Updated));
            b.Prop("mine", viewer != null && a.Owner == viewer.Id);
            b.Name("owner").Raw(UserSummary(_store.FindUser(a.Owner)));
            b.EndObject();
            return b.ToString();
        }

        private Album RequireAlbum(long id)
        {
            Album a = _store.FindAlbum(id);
            if (a == null) throw new ApiException(404, "no-album", "Album not found");
            return a;
        }

        private void HandleAlbums(ApiCtx ctx)
        {
            long userId = LongQuery(ctx, "user");
            User target = userId > 0 ? RequireUser(userId) : ctx.User;
            if (!CanViewProfile(ctx.User, target)) throw new ApiException(403, "private", "This profile is private");
            if (!Privacy.Allows(target, ctx.User, "albums", _store.AreFriends(ctx.User.Id, target.Id)))
                throw new ApiException(403, "albums-closed", "This user keeps their albums private");

            List<Album> albums = _store.AlbumsFor(target.Id);
            JsonBuilder b = new JsonBuilder(4096);
            b.BeginObject();
            b.Name("user").Raw(UserSummary(target));
            b.Name("albums").BeginArray();
            for (int i = 0; i < albums.Count; i++) b.Raw(AlbumJson(albums[i], ctx.User));
            b.EndArray();
            b.Prop("canCreate", target.Id == ctx.User.Id);
            SendOk(ctx, b);
        }

        private void HandleAlbum(ApiCtx ctx)
        {
            Album a = RequireAlbum(LongQuery(ctx, "id"));
            User owner = _store.FindUser(a.Owner);
            if (owner == null || owner.Deleted) throw new ApiException(404, "no-album", "Album not found");
            if (!CanViewProfile(ctx.User, owner)) throw new ApiException(403, "private", "This profile is private");
            if (!Privacy.Allows(owner, ctx.User, "albums", _store.AreFriends(ctx.User.Id, owner.Id)))
                throw new ApiException(403, "albums-closed", "This user keeps their albums private");

            long before = LongQuery(ctx, "before");
            int limit = LimitParam(ctx, 24, 60);
            List<Post> raw = _store.AlbumPhotos(a.Id, before, limit + 12);
            List<Post> photos = new List<Post>();
            for (int i = 0; i < raw.Count; i++)
            {
                if (!CanViewPost(ctx.User, raw[i])) continue;
                photos.Add(raw[i]);
                if (photos.Count > limit) break;
            }
            bool hasMore = photos.Count > limit;
            if (hasMore) photos.RemoveAt(photos.Count - 1);

            JsonBuilder b = new JsonBuilder(4096);
            b.BeginObject();
            b.Name("album").Raw(AlbumJson(a, ctx.User));
            AppendPhotos(b, "photos", photos);
            b.Prop("hasMore", hasMore);
            b.Prop("next", photos.Count == 0 ? 0 : photos[photos.Count - 1].Id);
            SendOk(ctx, b);
        }

        private void HandleAlbumSave(ApiCtx ctx)
        {
            long id = BodyLong(ctx, "id", 0);
            string name = BodyStr(ctx, "name", "").Trim();
            if (name.Length < 1) throw new ApiException(400, "bad-name", "An album needs a name");

            Album album;
            if (id > 0)
            {
                album = RequireAlbum(id);
                if (album.Owner != ctx.User.Id) throw new ApiException(403, "not-mine", "That is not your album");
                _store.UpdateAlbum(id, ctx.User.Id, name, BodyStr(ctx, "description", album.Description));
            }
            else
            {
                if (_store.CountAlbums(ctx.User.Id) >= 60)
                    throw new ApiException(400, "too-many", "You already have 60 albums");
                album = _store.CreateAlbum(ctx.User.Id, name, BodyStr(ctx, "description", ""));
                Log.Info("ALBUM", "@" + ctx.User.Username + " created album #" + album.Id);
            }

            JsonBuilder b = new JsonBuilder(512);
            b.BeginObject();
            b.Name("album").Raw(AlbumJson(album, ctx.User));
            SendOk(ctx, b);
        }

        private void HandleAlbumDelete(ApiCtx ctx)
        {
            long id = BodyLong(ctx, "id", 0);
            Album a = RequireAlbum(id);
            if (a.Owner != ctx.User.Id) throw new ApiException(403, "not-mine", "That is not your album");
            _store.DeleteAlbum(id, ctx.User.Id);
            Log.Info("ALBUM", "@" + ctx.User.Username + " deleted album #" + id);

            JsonBuilder b = new JsonBuilder(128);
            b.BeginObject();
            b.Prop("id", id);
            SendOk(ctx, b);
        }

        /// <summary>Moves one photo into an album, or out of every album with album=0.</summary>
        private void HandleAlbumAssign(ApiCtx ctx)
        {
            long postId = BodyLong(ctx, "post", 0);
            long albumId = BodyLong(ctx, "album", 0);

            Post p = _store.FindPost(postId);
            if (p == null || p.Deleted) throw new ApiException(404, "no-post", "Photo not found");
            if (p.Author != ctx.User.Id) throw new ApiException(403, "not-mine", "That is not your photo");
            if (string.IsNullOrEmpty(p.Image)) throw new ApiException(400, "no-image", "That post has no picture");
            if (albumId > 0)
            {
                Album a = RequireAlbum(albumId);
                if (a.Owner != ctx.User.Id) throw new ApiException(403, "not-mine", "That is not your album");
            }

            if (!_store.AssignPhoto(postId, albumId, ctx.User.Id))
                throw new ApiException(400, "failed", "The photo could not be moved");

            JsonBuilder b = new JsonBuilder(256);
            b.BeginObject();
            b.Prop("post", postId);
            b.Prop("album", albumId);
            SendOk(ctx, b);
        }

        /// <summary>Photos the user has not filed into any album yet.</summary>
        private void HandleAlbumUnfiled(ApiCtx ctx)
        {
            int limit = LimitParam(ctx, 24, 60);
            long before = LongQuery(ctx, "before");
            List<Post> raw = _store.PhotoPosts(ctx.User.Id, before, limit + 12);
            List<Post> list = new List<Post>();
            for (int i = 0; i < raw.Count && list.Count < limit; i++)
                if (raw[i].Album == 0) list.Add(raw[i]);

            JsonBuilder b = new JsonBuilder(4096);
            b.BeginObject();
            AppendPhotos(b, "photos", list);
            b.Prop("next", list.Count == 0 ? 0 : list[list.Count - 1].Id);
            SendOk(ctx, b);
        }
    }
}
