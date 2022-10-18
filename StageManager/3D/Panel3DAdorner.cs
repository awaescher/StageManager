using System;
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace StageManager;

/// <summary>
/// Hosts an opaque Viewport3D in the adorner layer above a Panel3D.
/// </summary>
public class Panel3DAdorner : Adorner
{
	#region Data

	private ArrayList _logicalChildren;
	private readonly DockPanel _viewportHost = new DockPanel();

	#endregion // Data

	#region Constructor

	public Panel3DAdorner(Panel3D adornedPanel3D, Viewport3D viewport)
		: base(adornedPanel3D)
	{
		_viewportHost.Children.Add(viewport);
		_viewportHost.Background = Brushes.Transparent;
		base.AddLogicalChild(_viewportHost);
		base.AddVisualChild(_viewportHost);


	}

	#endregion // Constructor

	#region Measure/Arrange

	///// <summary>
	///// Allows the control to determine how big it wants to be.
	///// </summary>
	///// <param name="constraint">A limiting size for the control.</param>
	//protected override Size MeasureOverride(Size constraint)
	//{
	//	//_viewport.Measure(constraint);
	//	//return _viewport.DesiredSize;

	//	//if (constraint.Width > 10000)
	//	//	constraint.Width = constraint.Height;

	//	//if (constraint.Height > 10000)
	//	//	constraint.Height = constraint.Width;

	//	//return constraint;

	//	return new Size(100, 100);
	//}

	/// <summary>
	/// Positions and sizes the control.
	/// </summary>
	/// <param name="finalSize">The actual size of the control.</param>		
	protected override Size ArrangeOverride(Size finalSize)
	{
		Rect rect = new Rect(new Point(), finalSize);

		_viewportHost.Arrange(rect);

		return finalSize;
	}

	#endregion // Measure/Arrange

	#region Visual Children

	/// <summary>
	/// Required for the element to be rendered.
	/// </summary>
	protected override int VisualChildrenCount
	{
		get { return 1; }
	}

	/// <summary>
	/// Required for the element to be rendered.
	/// </summary>
	protected override Visual GetVisualChild(int index)
	{
		if (index != 0)
			throw new ArgumentOutOfRangeException("index");

		return _viewportHost;
	}

	#endregion // Visual Children

	#region Logical Children

	/// <summary>
	/// Required for the displayed element to inherit property values
	/// from the logical tree, such as FontSize.
	/// </summary>
	protected override IEnumerator LogicalChildren
	{
		get
		{
			if (_logicalChildren == null)
			{
				_logicalChildren = new ArrayList();
				_logicalChildren.Add(_viewportHost);
			}

			return _logicalChildren.GetEnumerator();
		}
	}

	#endregion // Logical Children
}