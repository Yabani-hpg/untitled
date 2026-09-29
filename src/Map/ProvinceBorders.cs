using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Untitled.Core;
using Untitled.Data;

namespace Untitled.Map;

/// <summary>
/// Draws smooth province and country borders as screen-width ribbons over the flat map, and the
/// outline of the selected province. Country borders follow ownership: call <see cref="RefreshOwners"/>
/// after provinces change hands.
/// </summary>
public partial class ProvinceBorders : Node3D
{
	/// <summary>The flat map mesh; borders hide with it (e.g. in globe view) and copy its terrain parameters.</summary>
	[Export] public NodePath FlatWorldPath { get; set; }
	[Export] public Shader BorderShader { get; set; }
	[Export] public int ChunksX { get; set; } = 8;
	[Export] public int ChunksY { get; set; } = 4;

	static readonly StringName[] SharedTerrainParams = { "heightmap", "sea_level_norm", "height_scale", "terrain_base_y" };
	const int OwnerMapWidth = 256;

	MeshInstance3D _flatWorld;
	float _mapWidth;
	ulong _buildStart;
	Task<List<ChunkBuilder>> _build;
	ShaderMaterial _material;
	Image _ownerImage;
	ImageTexture _ownerTexture;

	public override void _Ready()
	{
		GameState state = GameState.Instance;
		_flatWorld = GetNodeOrNull<MeshInstance3D>(FlatWorldPath);
		if (state?.ProvinceMap == null || _flatWorld?.Mesh is not PlaneMesh plane || BorderShader == null)
		{
			GD.PushError($"{nameof(ProvinceBorders)}: needs the GameState autoload, FlatWorldPath with a PlaneMesh, and BorderShader");
			return;
		}

		_mapWidth = plane.Size.X;
		_buildStart = Time.GetTicksMsec();
		var isLand = new bool[state.Provinces.Count];
		for (int i = 0; i < isLand.Length; i++)
			isLand[i] = state.Provinces[i] is { IsWater: false };
		// tracing ~200k border edges takes a moment; do it off the main thread so the map shows at once
		ProvinceMap map = state.ProvinceMap;
		_build = Task.Run(() => BuildChunks(ProvinceBorderBuilder.Build(map, isLand), map));

		_material = new ShaderMaterial { Shader = BorderShader };
		if (_flatWorld.Mesh.SurfaceGetMaterial(0) is ShaderMaterial terrain)
		{
			foreach (StringName param in SharedTerrainParams)
				_material.SetShaderParameter(param, terrain.GetShaderParameter(param));
		}
		_material.SetShaderParameter("map_size", plane.Size);
		BuildOwnerMap(state);
		state.OwnershipChanged += RefreshOwners;
		_material.SetShaderParameter("owner_map", _ownerTexture);

		state.SelectedProvinceChanged += OnSelectedProvinceChanged;
		OnSelectedProvinceChanged(state.SelectedProvinceId);
	}

	void AddMeshes(List<ChunkBuilder> chunks)
	{
		var meshes = chunks.Select(c => c.ToMesh()).ToList();
		// the map wraps east-west: one copy per side covers everything the camera can see at border zooms
		foreach (int offset in new[] { -1, 0, 1 })
		{
			var copy = new Node3D { Name = $"Copy{offset:+0;-0;0}", Position = new Vector3(offset * _mapWidth, 0f, 0f) };
			AddChild(copy);
			foreach (ArrayMesh mesh in meshes)
			{
				copy.AddChild(new MeshInstance3D
				{
					Mesh = mesh,
					MaterialOverride = _material,
					CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
					ExtraCullMargin = 40f, // the vertex shader lifts and widens the ribbons
				});
			}
		}
		int vertices = chunks.Sum(c => c.Vertices.Count);
		GD.Print($"ProvinceBorders: {vertices} vertices in {meshes.Count} chunks, ready {Time.GetTicksMsec() - _buildStart} ms after load");
	}

	public override void _ExitTree()
	{
		if (GameState.Instance != null && _material != null)
		{
			GameState.Instance.SelectedProvinceChanged -= OnSelectedProvinceChanged;
			GameState.Instance.OwnershipChanged -= RefreshOwners;
		}
	}

	public override void _Process(double delta)
	{
		if (_build is { IsCompleted: true })
		{
			if (_build.IsFaulted)
				GD.PushError($"{nameof(ProvinceBorders)}: {_build.Exception?.InnerException}");
			else
				AddMeshes(_build.Result);
			_build = null;
		}
		if (_flatWorld != null)
			Visible = _flatWorld.IsVisibleInTree();
	}

	/// <summary>Re-reads every province's owner, so country borders move after conquests.</summary>
	public void RefreshOwners()
	{
		if (_ownerImage == null)
			return;
		FillOwnerImage(GameState.Instance);
		_ownerTexture.Update(_ownerImage);
	}

	void OnSelectedProvinceChanged(int provinceId) =>
		_material.SetShaderParameter("selected_province", GameState.Instance.GetProvince(provinceId) == null ? -1f : provinceId);

	void BuildOwnerMap(GameState state)
	{
		int rows = (state.Provinces.Count + OwnerMapWidth - 1) / OwnerMapWidth;
		_ownerImage = Image.CreateEmpty(OwnerMapWidth, rows, false, Image.Format.Rg8);
		FillOwnerImage(state);
		_ownerTexture = ImageTexture.CreateFromImage(_ownerImage);
	}

	void FillOwnerImage(GameState state)
	{
		var index = state.Countries.Keys.OrderBy(t => t, System.StringComparer.Ordinal)
			.Select((tag, i) => (tag, i)).ToDictionary(x => x.tag, x => x.i + 1);
		_ownerImage.Fill(new Color(0, 0, 0));
		foreach (Province p in state.Provinces)
		{
			if (p == null)
				continue;
			// all unowned land shares one code, so borders between unowned provinces stay province borders
			int owner = p.OwnerTag != null && index.TryGetValue(p.OwnerTag, out int i) ? i : 0xFFFF;
			_ownerImage.SetPixel(p.Id % OwnerMapWidth, p.Id / OwnerMapWidth,
				Color.Color8((byte)(owner & 255), (byte)(owner >> 8), 0));
		}
	}

	/// <summary>Ribbon geometry per chunk. Pure data, so it runs on a worker thread.</summary>
	List<ChunkBuilder> BuildChunks(List<ProvinceBorderBuilder.Polyline> borders, ProvinceMap map)
	{
		int chunks = ChunksX * ChunksY;
		var builders = new ChunkBuilder[chunks];
		for (int i = 0; i < chunks; i++)
			builders[i] = new ChunkBuilder();

		var half = new Vector2(map.Width, map.Height) * 0.5f;
		foreach (var border in borders)
		{
			Vector2 mid = border.Points[border.Points.Length / 2];
			int cx = Mathf.Clamp((int)(mid.X / map.Width * ChunksX), 0, ChunksX - 1);
			int cy = Mathf.Clamp((int)(mid.Y / map.Height * ChunksY), 0, ChunksY - 1);
			builders[cy * ChunksX + cx].AddRibbon(border, half);
		}

		return builders.Where(b => b.Vertices.Count > 0).ToList();
	}

	sealed class ChunkBuilder
	{
		public readonly List<Vector3> Vertices = new();
		readonly List<Vector3> _normals = new();
		readonly List<Vector2> _uv = new();
		readonly List<Vector2> _uv2 = new();
		readonly List<int> _indices = new();

		public void AddRibbon(ProvinceBorderBuilder.Polyline border, Vector2 half)
		{
			Vector2[] p = border.Points;
			int n = p.Length;
			if (n < 2)
				return;
			bool closed = n > 2 && p[0] == p[n - 1];
			var ids = new Vector2(border.A, border.B);
			int first = Vertices.Count;
			int emitted = 0;
			for (int i = 0; i < n; i++)
			{
				Vector2 prev = i > 0 ? p[i - 1] : closed ? p[n - 2] : p[i];
				Vector2 next = i < n - 1 ? p[i + 1] : closed ? p[1] : p[i];
				Vector2 dIn = (p[i] - prev).Normalized();
				Vector2 dOut = (next - p[i]).Normalized();
				if (dIn == Vector2.Zero) dIn = dOut;
				if (dOut == Vector2.Zero) dOut = dIn;
				if (dIn == Vector2.Zero)
					continue;
				Vector2 nIn = new(-dIn.Y, dIn.X), nOut = new(-dOut.Y, dOut.X);
				Vector2 miter = (nIn + nOut).Normalized();
				if (miter == Vector2.Zero)
					miter = nOut;
				float scale = Mathf.Min(1f / Mathf.Max(miter.Dot(nOut), 0.2f), 2.5f);

				var pos = new Vector3(p[i].X - half.X, 0f, p[i].Y - half.Y);
				var normal = new Vector3(miter.X, 0f, miter.Y);
				for (int side = -1; side <= 1; side += 2)
				{
					Vertices.Add(pos);
					_normals.Add(normal);
					_uv.Add(new Vector2(side, scale));
					_uv2.Add(ids);
				}
				if (emitted > 0)
				{
					int v = first + 2 * emitted;
					_indices.Add(v - 2); _indices.Add(v - 1); _indices.Add(v);
					_indices.Add(v - 1); _indices.Add(v + 1); _indices.Add(v);
				}
				emitted++;
			}
		}

		public ArrayMesh ToMesh()
		{
			var arrays = new Godot.Collections.Array();
			arrays.Resize((int)Mesh.ArrayType.Max);
			arrays[(int)Mesh.ArrayType.Vertex] = Vertices.ToArray();
			arrays[(int)Mesh.ArrayType.Normal] = _normals.ToArray();
			arrays[(int)Mesh.ArrayType.TexUV] = _uv.ToArray();
			arrays[(int)Mesh.ArrayType.TexUV2] = _uv2.ToArray();
			arrays[(int)Mesh.ArrayType.Index] = _indices.ToArray();
			var mesh = new ArrayMesh();
			mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
			return mesh;
		}
	}
}
