using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Dunia.Formats.Meshes;
using NumericsVector3 = System.Numerics.Vector3;

namespace Dunia.Toolkit;

public partial class MeshPreviewWindow : Window
{
    private static readonly Color[] MaterialColors =
    [
        Color.FromRgb(91, 169, 230),
        Color.FromRgb(230, 145, 91),
        Color.FromRgb(113, 197, 138),
        Color.FromRgb(194, 125, 212),
        Color.FromRgb(222, 197, 92),
        Color.FromRgb(89, 194, 190),
        Color.FromRgb(221, 111, 133),
        Color.FromRgb(156, 164, 179),
    ];

    private readonly DirectionalLight _cameraLight = new(Colors.White, new Vector3D(-1, 1, -1));
    private readonly XbgMeshPreview _mesh;
    private Point3D _target;
    private Point _lastPointer;
    private double _yaw = -Math.PI / 4;
    private double _pitch = Math.PI / 7;
    private double _distance;
    private double _fitDistance;
    private InteractionMode _interaction;

    public MeshPreviewWindow(XbgMeshPreview mesh, string displayName)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        _mesh = mesh;
        InitializeComponent();
        Title = $"Mesh preview - {displayName}";
        MeshNameText.Text = displayName;
        MeshNameText.ToolTip = displayName;
        LodSelector.ItemsSource = mesh.Lods
            .Select((lod, index) => new LodOption(
                index,
                $"LOD {index}  •  {lod.Positions.Count:N0} vertices  •  {lod.TriangleCount:N0} triangles"))
            .ToArray();
        LodSelector.SelectedIndex = 0;
        Loaded += WindowLoaded;
    }

    private void WindowLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= WindowLoaded;
        MeshViewport.Focus();
    }

    private void LodSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LodSelector.SelectedIndex >= 0)
        {
            BuildScene(LodSelector.SelectedIndex);
        }
    }

    private void BuildScene(int lodIndex)
    {
        XbgMeshLod lod = _mesh.Lods[lodIndex];
        MeshStatsText.Text = $"LOD {lodIndex + 1:N0} of {_mesh.Lods.Count:N0}  •  distance {lod.Distance:N2}  •  {lod.Positions.Count:N0} vertices  •  {lod.TriangleCount:N0} triangles  •  {lod.Sections.Count:N0} sections";
        var positions = new Point3DCollection(lod.Positions.Count);
        var normals = new Vector3DCollection(lod.Normals.Count);
        var textureCoordinates = new PointCollection(lod.TextureCoordinates.Count);

        NumericsVector3 min = new(float.PositiveInfinity);
        NumericsVector3 max = new(float.NegativeInfinity);
        for (int i = 0; i < lod.Positions.Count; i++)
        {
            NumericsVector3 p = lod.Positions[i];
            NumericsVector3 n = lod.Normals[i];
            System.Numerics.Vector2 uv = lod.TextureCoordinates[i];
            positions.Add(new(p.X, p.Y, p.Z));
            normals.Add(new(n.X, n.Y, n.Z));
            textureCoordinates.Add(new(uv.X, uv.Y));
            min = NumericsVector3.Min(min, p);
            max = NumericsVector3.Max(max, p);
        }

        positions.Freeze();
        normals.Freeze();
        textureCoordinates.Freeze();

        var model = new Model3DGroup();
        model.Children.Add(new AmbientLight(Color.FromRgb(80, 84, 92)));
        model.Children.Add(_cameraLight);
        for (int i = 0; i < lod.Sections.Count; i++)
        {
            XbgMeshSection section = lod.Sections[i];
            var triangleIndices = new Int32Collection(section.TriangleIndices.Count);
            foreach (int index in section.TriangleIndices)
            {
                triangleIndices.Add(index);
            }

            triangleIndices.Freeze();
            var geometry = new MeshGeometry3D
            {
                Positions = positions,
                Normals = normals,
                TextureCoordinates = textureCoordinates,
                TriangleIndices = triangleIndices,
            };
            geometry.Freeze();

            int colorIndex = Math.Abs(section.MaterialIndex % MaterialColors.Length);
            MaterialGroup material = CreateMaterial(MaterialColors[colorIndex]);
            model.Children.Add(new GeometryModel3D(geometry, material) { BackMaterial = material });
        }

        MeshViewport.Children.Clear();
        MeshViewport.Children.Add(new ModelVisual3D { Content = model });

        NumericsVector3 center = (min + max) * 0.5f;
        double radius = Math.Max((max - min).Length() * 0.5, 0.01);
        _target = new(center.X, center.Y, center.Z);
        _fitDistance = Math.Max(radius / Math.Tan(Camera.FieldOfView * Math.PI / 360) * 1.2, 0.1);
        Camera.NearPlaneDistance = Math.Max(radius / 10_000, 0.0001);
        Camera.FarPlaneDistance = Math.Max(radius * 10_000, 1000);
        ResetView();
    }

    private static MaterialGroup CreateMaterial(Color color)
    {
        var group = new MaterialGroup();
        group.Children.Add(new DiffuseMaterial(new SolidColorBrush(color)));
        group.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)), 24));
        group.Freeze();
        return group;
    }

    private void ResetViewClick(object sender, RoutedEventArgs e)
    {
        ResetView();
        MeshViewport.Focus();
    }

    private void ResetView()
    {
        _yaw = -Math.PI / 4;
        _pitch = Math.PI / 7;
        _distance = _fitDistance;
        UpdateCamera();
    }

    private void ViewportMouseDown(object sender, MouseButtonEventArgs e)
    {
        _interaction = e.ChangedButton switch
        {
            MouseButton.Left => InteractionMode.Orbit,
            MouseButton.Right or MouseButton.Middle => InteractionMode.Pan,
            _ => InteractionMode.None,
        };
        if (_interaction == InteractionMode.None)
        {
            return;
        }

        _lastPointer = e.GetPosition(MeshViewport);
        MeshViewport.Focus();
        Mouse.Capture(MeshViewport);
        e.Handled = true;
    }

    private void ViewportMouseMove(object sender, MouseEventArgs e)
    {
        if (_interaction == InteractionMode.None || e.LeftButton == MouseButtonState.Released &&
            e.RightButton == MouseButtonState.Released && e.MiddleButton == MouseButtonState.Released)
        {
            return;
        }

        Point current = e.GetPosition(MeshViewport);
        Vector delta = current - _lastPointer;
        _lastPointer = current;
        if (_interaction == InteractionMode.Orbit)
        {
            _yaw -= delta.X * 0.008;
            _pitch = Math.Clamp(_pitch + (delta.Y * 0.008), -1.45, 1.45);
            UpdateCamera();
        }
        else
        {
            Pan(delta.X, delta.Y);
        }

        e.Handled = true;
    }

    private void ViewportMouseUp(object sender, MouseButtonEventArgs e)
    {
        EndInteraction();
        e.Handled = true;
    }

    private void ViewportLostMouseCapture(object sender, MouseEventArgs e) => _interaction = InteractionMode.None;

    private void EndInteraction()
    {
        _interaction = InteractionMode.None;
        if (ReferenceEquals(Mouse.Captured, MeshViewport))
        {
            Mouse.Capture(null);
        }
    }

    private void ViewportMouseWheel(object sender, MouseWheelEventArgs e)
    {
        Zoom(e.Delta > 0 ? 0.85 : 1 / 0.85);
        e.Handled = true;
    }

    private void WindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        const double orbitStep = 0.08;
        if (LodSelector.IsKeyboardFocusWithin &&
            (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home ||
             e.Key == Key.Escape && LodSelector.IsDropDownOpen))
        {
            return;
        }

        bool pan = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        switch (e.Key)
        {
            case Key.Left when pan:
                Pan(-24, 0);
                break;
            case Key.Right when pan:
                Pan(24, 0);
                break;
            case Key.Up when pan:
                Pan(0, -24);
                break;
            case Key.Down when pan:
                Pan(0, 24);
                break;
            case Key.Left:
                _yaw += orbitStep;
                UpdateCamera();
                break;
            case Key.Right:
                _yaw -= orbitStep;
                UpdateCamera();
                break;
            case Key.Up:
                _pitch = Math.Clamp(_pitch - orbitStep, -1.45, 1.45);
                UpdateCamera();
                break;
            case Key.Down:
                _pitch = Math.Clamp(_pitch + orbitStep, -1.45, 1.45);
                UpdateCamera();
                break;
            case Key.Add or Key.OemPlus:
                Zoom(0.85);
                break;
            case Key.Subtract or Key.OemMinus:
                Zoom(1 / 0.85);
                break;
            case Key.Home:
                ResetView();
                break;
            case Key.OemOpenBrackets when LodSelector.SelectedIndex > 0:
                LodSelector.SelectedIndex--;
                break;
            case Key.OemCloseBrackets when LodSelector.SelectedIndex + 1 < LodSelector.Items.Count:
                LodSelector.SelectedIndex++;
                break;
            case Key.Escape:
                Close();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void Zoom(double factor)
    {
        _distance = Math.Clamp(_distance * factor, _fitDistance * 0.02, _fitDistance * 100);
        UpdateCamera();
    }

    private void Pan(double horizontalPixels, double verticalPixels)
    {
        Vector3D look = Camera.LookDirection;
        look.Normalize();
        Vector3D right = Vector3D.CrossProduct(look, Camera.UpDirection);
        right.Normalize();
        Vector3D up = Vector3D.CrossProduct(right, look);
        up.Normalize();
        double scale = _distance * 2 * Math.Tan(Camera.FieldOfView * Math.PI / 360) /
                       Math.Max(MeshViewport.ActualHeight, 1);
        Vector3D shift = (-horizontalPixels * scale * right) + (verticalPixels * scale * up);
        _target += shift;
        UpdateCamera();
    }

    private void UpdateCamera()
    {
        double horizontal = _distance * Math.Cos(_pitch);
        var offset = new Vector3D(
            horizontal * Math.Cos(_yaw),
            horizontal * Math.Sin(_yaw),
            _distance * Math.Sin(_pitch));
        Camera.Position = _target + offset;
        Camera.LookDirection = -offset;
        Camera.UpDirection = new(0, 0, 1);
        _cameraLight.Direction = Camera.LookDirection;
    }

    private enum InteractionMode
    {
        None,
        Orbit,
        Pan,
    }

    private sealed record LodOption(int Index, string Label)
    {
        public override string ToString() => Label;
    }
}
