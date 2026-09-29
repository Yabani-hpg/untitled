using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Godot;
using Untitled.Data;

namespace Untitled.Core;

/// <summary>A save file as listed in the load screen, read from its first line only.</summary>
public sealed record SaveInfo(
	string Path, string Name, string PlayerTag, string CountryName, string FlagImage, string RulerName,
	long Day, string DateText, string SavedAt, string ThumbnailPath);

/// <summary>
/// Save files in user://saves, as in Paradox games: named by the player, one per name, plus a yearly
/// autosave. A save is a text file of two JSON lines: a small header (country, flag, ruler, date) that the
/// load screen lists without reading the rest, then the game itself (<see cref="GameState.WriteSave"/>).
/// A screenshot saved next to it (name.png) is the load screen's preview.
/// </summary>
public static class SaveGames
{
	public const string Dir = "user://saves";
	public const string Extension = ".sav";
	public const string AutosaveName = "autosave";
	const int Format = 1;
	static readonly Vector2I ThumbnailSize = new(480, 270);

	public static string PathFor(string name) => $"{Dir}/{Sanitize(name)}{Extension}";

	/// <summary>A default name for a new save: "Egypt 1200 BC".</summary>
	public static string SuggestName(GameState gs)
	{
		string country = gs.PlayerCountry?.Name ?? "Game";
		CalendarDate d = gs.Definitions.DefaultCalendar.Convert(gs.Date);
		return $"{country} {d.Year} {d.Era}";
	}

	/// <summary>Saves the game as <paramref name="name"/>, overwriting a save of that name.</summary>
	public static bool Save(GameState gs, string name, Image screenshot, out string error)
	{
		error = null;
		name = Sanitize(name);
		if (name.Length == 0)
		{
			error = "Enter a name for the save";
			return false;
		}
		try
		{
			DirAccess.MakeDirRecursiveAbsolute(Dir);
			using var stream = new MemoryStream();
			using (var w = new Utf8JsonWriter(stream))
				WriteHeader(w, gs, name);
			stream.WriteByte((byte)'\n');
			using (var w = new Utf8JsonWriter(stream))
				gs.WriteSave(w);
			stream.WriteByte((byte)'\n');

			string path = PathFor(name);
			using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write);
			if (file == null)
			{
				error = $"Cannot write {path}: {Godot.FileAccess.GetOpenError()}";
				return false;
			}
			file.StoreBuffer(stream.ToArray());
			if (screenshot != null)
			{
				Image thumb = (Image)screenshot.Duplicate();
				thumb.Resize(ThumbnailSize.X, ThumbnailSize.Y, Image.Interpolation.Bilinear);
				thumb.SavePng(ThumbnailFor(path));
			}
			GD.Print($"Saved {path}");
			return true;
		}
		catch (Exception e)
		{
			error = e.Message;
			return false;
		}
	}

	/// <summary>The yearly autosave, overwritten each time.</summary>
	public static void Autosave(GameState gs)
	{
		Image screenshot = gs.GetViewport()?.GetTexture()?.GetImage();
		if (!Save(gs, AutosaveName, screenshot, out string error))
			GD.PushWarning($"Autosave failed: {error}");
	}

	/// <summary>Loads a save into the game state. The caller then (re)loads the map scene.</summary>
	public static bool Load(GameState gs, string path, out string error)
	{
		error = null;
		try
		{
			using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
			if (file == null)
			{
				error = $"Cannot open {path}: {Godot.FileAccess.GetOpenError()}";
				return false;
			}
			file.GetLine();                                     // the header
			using JsonDocument doc = JsonDocument.Parse(file.GetLine());
			gs.ReadSave(doc.RootElement);
			GD.Print($"Loaded {path}: {gs.PlayerTag}, {gs.DateText}");
			return true;
		}
		catch (Exception e)
		{
			error = $"This save cannot be loaded ({e.Message})";
			GD.PushError($"Loading {path}: {e}");
			return false;
		}
	}

	/// <summary>All saves, the most recently saved first.</summary>
	public static List<SaveInfo> List()
	{
		var saves = new List<SaveInfo>();
		if (!DirAccess.DirExistsAbsolute(Dir))
			return saves;
		foreach (string file in DirAccess.GetFilesAt(Dir))
		{
			if (!file.EndsWith(Extension))
				continue;
			string path = $"{Dir}/{file}";
			SaveInfo info = ReadHeader(path);
			if (info != null)
				saves.Add(info);
		}
		saves.Sort((a, b) => string.CompareOrdinal(b.SavedAt, a.SavedAt));
		return saves;
	}

	public static void Delete(SaveInfo save)
	{
		DirAccess.RemoveAbsolute(save.Path);
		if (Godot.FileAccess.FileExists(save.ThumbnailPath))
			DirAccess.RemoveAbsolute(save.ThumbnailPath);
	}

	public static bool Exists(string name) => Godot.FileAccess.FileExists(PathFor(name));

	/// <summary>The thumbnail as a texture, or null.</summary>
	public static Texture2D LoadThumbnail(SaveInfo save)
	{
		if (!Godot.FileAccess.FileExists(save.ThumbnailPath))
			return null;
		Image image = Image.LoadFromFile(save.ThumbnailPath);
		return image == null ? null : ImageTexture.CreateFromImage(image);
	}

	static void WriteHeader(Utf8JsonWriter w, GameState gs, string name)
	{
		Country player = gs.PlayerCountry;
		w.WriteStartObject();
		w.WriteNumber("format", Format);
		w.WriteString("name", name);
		w.WriteString("player", player?.Tag);
		w.WriteString("country", player?.Name);
		w.WriteString("flag", player?.CurrentFlag?.ImagePath);
		w.WriteString("ruler", player?.Ruler != null ? $"{player.RulerTitle} {player.Ruler.Name}" : null);
		w.WriteNumber("date", gs.Date.Day);
		w.WriteString("date_text", gs.DateText);
		w.WriteString("saved", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
		w.WriteEndObject();
	}

	static SaveInfo ReadHeader(string path)
	{
		try
		{
			using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
			if (file == null)
				return null;
			using JsonDocument doc = JsonDocument.Parse(file.GetLine());
			JsonElement h = doc.RootElement;
			string S(string key) => h.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
			return new SaveInfo(path, S("name"), S("player"), S("country"), S("flag"), S("ruler"),
				h.GetProperty("date").GetInt64(), S("date_text"), S("saved") ?? "", ThumbnailFor(path));
		}
		catch (Exception e)
		{
			GD.PushWarning($"{path} is not a readable save: {e.Message}");
			return null;
		}
	}

	static string ThumbnailFor(string path) => path[..^Extension.Length] + ".png";

	/// <summary>Keeps letters, digits, spaces and a few marks, so the name is a valid file name everywhere.</summary>
	static string Sanitize(string name)
	{
		var sb = new StringBuilder();
		foreach (char c in name ?? "")
		{
			if (char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' or '(' or ')' or ',' or '.')
				sb.Append(c);
		}
		return sb.ToString().Trim().TrimEnd('.');
	}
}
