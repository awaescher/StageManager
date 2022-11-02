// TAKEN FROM https://joshsmithonwpf.wordpress.com/2008/03/30/animating-images-in-a-3d-itemscontrol/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Media3D;

namespace StageManager;

/// <summary>
/// A Panel that displays its children in a 
/// Viewport3D hosted in the adorner layer.
/// </summary>
public class Panel3D : Panel
{
	#region Data

	static readonly Point ORIGIN_POINT = new Point(0, 0);

	bool _isMovingItems;
	int _registeredNameCounter = 0;
	readonly Viewport3D _viewport;
	readonly Dictionary<DependencyObject, ModelVisual3D> _visualTo3DModelMap = new Dictionary<DependencyObject, ModelVisual3D>();

	#endregion // Data

	#region Constructor

	public Panel3D()
	{
		_viewport = Application.LoadComponent(new Uri("3D/Scene.xaml", UriKind.Relative)) as Viewport3D;

		this.Loaded += delegate
		{
			var adornerLayer = AdornerLayer.GetAdornerLayer(this);
			var adorner = new Panel3DAdorner(this, _viewport);
			
			adornerLayer.IsHitTestVisible = false;
			adorner.IsHitTestVisible = false;

			adornerLayer.Add(adorner);
		};
	}

	protected override void OnInitialized(EventArgs e)
	{
		base.OnInitialized(e);
		_viewport.DataContext = this.DataContext;
	}

	protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
	{
		base.OnPropertyChanged(e);

		if (e.Property.Name == nameof(IsMouseOver))
			_viewport.Opacity = ((bool)e.NewValue) ? 1.0 : 0.8;
		else if (e.Property.Name == nameof(IsVisible))
			_viewport.Visibility = ((bool)e.NewValue) ? Visibility.Visible : Visibility.Hidden;
	}

	#endregion // Constructor

	#region MoveItems

	public void MoveItems(bool forward)
	{
		if (_isMovingItems)
			return;

		// We cannot move items less than two items.
		if (_viewport.Children.Count < 2)
			return;

		#region Create Lists

		// Get a list of all the GeometryModel3D and TranslateTransform3D objects in the viewport.
		List<GeometryModel3D> geometries = new List<GeometryModel3D>();
		List<TranslateTransform3D> transforms = new List<TranslateTransform3D>();
		foreach (ModelVisual3D model in _viewport.Children)
		{
			GeometryModel3D geo = model.Content as GeometryModel3D;
			if (geo != null)
			{
				geometries.Add(geo);
				transforms.Add(geo.Transform as TranslateTransform3D);
			}
		}

		#endregion // Create Lists

		#region Relocate Target Item

		// Move the first or last item to the opposite end of the list.
		if (forward)
		{
			var firstGeo = geometries[0];
			geometries.RemoveAt(0);
			geometries.Add(firstGeo);

			// The item at index 0 holds the scene's light
			// so don't remove that, instead remove the first
			// model that we added to the scene in code.
			var firstChild = _viewport.Children[1];
			_viewport.Children.RemoveAt(1);
			_viewport.Children.Add(firstChild);
		}
		else
		{
			int idx = geometries.Count - 1;
			var lastGeo = geometries[idx];
			geometries.RemoveAt(idx);
			geometries.Insert(0, lastGeo);

			idx = _viewport.Children.Count - 1;
			var lastChild = _viewport.Children[idx];
			_viewport.Children.RemoveAt(idx);
			_viewport.Children.Insert(1, lastChild);
		}

		#endregion // Relocate Target Item

		#region Animate All Items to New Locations and Opacitys

		Storyboard story = new Storyboard();

		// Apply the new transforms via animations.
		for (int i = 0; i < transforms.Count; ++i)
		{
			double targetX = transforms[i].OffsetX;
			double targetY = transforms[i].OffsetY;
			double targetZ = transforms[i].OffsetZ;

			var trans = geometries[i].Transform as TranslateTransform3D;

			// In order to animate the transform it must have a name registered 
			// with the viewport.  Since a transform does not have a Name property
			// I just create an arbitrary name for each transform and register that.
			string name = this.GetNextName();
			_viewport.RegisterName(name, trans);

			Duration duration = new Duration(TimeSpan.FromSeconds(1.25));

			DoubleAnimation animX = new DoubleAnimation();
			animX.To = targetX;
			animX.Duration = duration;
			animX.AccelerationRatio = 0.1;
			animX.DecelerationRatio = 0.9;
			Storyboard.SetTargetProperty(animX, new PropertyPath("OffsetX"));
			Storyboard.SetTargetName(animX, name);
			story.Children.Add(animX);

			DoubleAnimation animY = new DoubleAnimation();
			animY.To = targetY;
			animY.AccelerationRatio = 0.7;
			animY.DecelerationRatio = 0.3;
			animY.Duration = duration;
			Storyboard.SetTargetProperty(animY, new PropertyPath("OffsetY"));
			Storyboard.SetTargetName(animY, name);
			story.Children.Add(animY);

			DoubleAnimation animZ = new DoubleAnimation();
			animZ.To = targetZ;
			animZ.AccelerationRatio = 0.3;
			animZ.DecelerationRatio = 0.7;
			animZ.Duration = duration;
			Storyboard.SetTargetProperty(animZ, new PropertyPath("OffsetZ"));
			Storyboard.SetTargetName(animZ, name);
			story.Children.Add(animZ);

			DoubleAnimation animOpacity = new DoubleAnimation();
			var material = geometries[i].Material as DiffuseMaterial;
			var brush = material.Brush as VisualBrush;
			name = this.GetNextName();
			_viewport.RegisterName(name, brush);
			animOpacity.To = 1.0 - (i / (double)transforms.Count);
			animOpacity.AccelerationRatio = 0.2;
			animOpacity.DecelerationRatio = 0.8;
			animOpacity.Duration = duration;
			Storyboard.SetTargetProperty(animOpacity, new PropertyPath("Opacity"));
			Storyboard.SetTargetName(animOpacity, name);
			story.Children.Add(animOpacity);

			if (i == 0)
				animX.Completed += delegate { _isMovingItems = false; };
		}

		_isMovingItems = true;
		story.Begin(_viewport);

		#endregion // Animate All Items to New Locations and Opacitys
	}

	string GetNextName()
	{
		return "name" + _registeredNameCounter++;
	}

	#endregion // MoveItems

	#region DPs

	public static readonly DependencyProperty ElementHeightProperty =
		DependencyProperty.Register("ElementHeight", typeof(double), typeof(Panel3D),
									new FrameworkPropertyMetadata(30.0));

	public static readonly DependencyProperty ElementWidthProperty =
		DependencyProperty.Register("ElementWidth", typeof(double), typeof(Panel3D),
									new FrameworkPropertyMetadata(40.0));


	public double ElementWidth
	{
		get { return (double)GetValue(ElementWidthProperty); }
		set { SetValue(ElementWidthProperty, value); }
	}

	public double ElementHeight
	{
		get { return (double)GetValue(ElementHeightProperty); }
		set { SetValue(ElementHeightProperty, value); }
	}
	#endregion

	#region Layout Overrides

	protected override Size ArrangeOverride(Size finalSize)
	{
		Size size = new Size(ElementWidth, ElementHeight);

		// Arrange children so that their visualbrush has a valid width/height.
		foreach (UIElement child in Children)
			child.Arrange(new Rect(ORIGIN_POINT, size));

		_viewport.Arrange(new Rect(ORIGIN_POINT, finalSize));

		return finalSize;
	}

	protected override Size MeasureOverride(Size availableSize)
	{
		Size size = new Size(ElementWidth, ElementHeight);

		foreach (UIElement child in Children)
			child.Measure(size);

		_viewport.Measure(availableSize);

		if (availableSize.Width > 10000)
			availableSize.Width = availableSize.Height;

		if (availableSize.Height > 10000)
			availableSize.Height = availableSize.Width;

		return size;
	}

	#endregion  // Layout Overrides

	#region OnVisualChildrenChanged

	protected override void OnVisualChildrenChanged(DependencyObject visualAdded, DependencyObject visualRemoved)
	{
		base.OnVisualChildrenChanged(visualAdded, visualRemoved);

		bool add = visualAdded != null && !_visualTo3DModelMap.ContainsKey(visualAdded);
		if (add)
		{
			// effects the PerspectiveCamera in Scene.xaml like for 20 here its Position="-3,0,3.5" and for 100 its  Position="-1.7,0,2.1"
			(visualAdded as ContentPresenter).Effect = new DropShadowEffect { BlurRadius = 30, Direction = 200 /* pretty leftish */ }; 
			var model = Build3DModel(visualAdded as Visual);
			_visualTo3DModelMap.Add(visualAdded, model);
			_viewport.Children.Add(model);
		}

		bool remove = visualRemoved != null && _visualTo3DModelMap.ContainsKey(visualRemoved);
		if (remove)
		{
			var model = _visualTo3DModelMap[visualRemoved];
			_viewport.Children.Remove(model);
			_visualTo3DModelMap.Remove(visualRemoved);
		}
	}

	#endregion // OnVisualChildrenChanged

	#region Build3DModel

	ModelVisual3D Build3DModel(Visual visual)
	{
		double opacityDivisor = 1;
			//_viewport.Children.Count > 0 ?
			//_viewport.Children.Count : 1;
			
		var model = new ModelVisual3D
		{
			Content = new GeometryModel3D
			{
				Geometry = new MeshGeometry3D
				{
					TriangleIndices = new Int32Collection(
						new int[] { 0, 1, 2, 2, 3, 0 }),
					TextureCoordinates = new PointCollection(
						new Point[]
						{
							new Point(0, 1),
							new Point(1, 1),
							new Point(1, 0),
							new Point(0, 0)
						}),
					Positions = new Point3DCollection(
						new Point3D[]
						{
							new Point3D(-1, -1, 0),
							new Point3D(1, -1, 0),
							new Point3D(1, 1, 0),
							new Point3D(-1, 1, 0)
						})
				},
				Material = new DiffuseMaterial
				{
					Brush = new VisualBrush
					{
						Visual = visual,
						Opacity = 1.0 / opacityDivisor
					}
				},
				Transform = new TranslateTransform3D
				{
					OffsetX = _viewport.Children.Count * 0.1,
					OffsetY = 0,
					OffsetZ = _viewport.Children.Count * 0.2
				}
			}
		};

		return model;
	}

	#endregion // Build3DModel
}

