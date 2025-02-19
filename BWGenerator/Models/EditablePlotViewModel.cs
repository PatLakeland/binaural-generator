﻿using System;
using System.Collections.Generic;

using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

using SharedLibrary.Code;

namespace BWGenerator.Models
{
    public sealed class EditablePlotViewModel<T> where T : BaseDataPoint
    {
        public delegate double ValueGetterDelegate(BaseDataPoint point);
        public delegate void ValueSetterDelegate(BaseDataPoint point, double value);
        public delegate BaseDataPoint PointCreatorDelegate(double time);
        public delegate void PointsUpdatedDelegate(object sender, EventArgs e);

        public event PointsUpdatedDelegate PointsUpdated;

        public PlotModel Model { get; }

        private ValueGetterDelegate getter = null;
        private ValueSetterDelegate setter = null;
        private PointCreatorDelegate creator = null;

        private LineSeries currentSerie = null;
        private int indexOfPointToMove = -1;
        private List<T> dataPoints = null;

        public EditablePlotViewModel(List<T> dataPoints, ValueGetterDelegate getter, ValueSetterDelegate setter, PointCreatorDelegate creator)
        {
            this.getter = getter;
            this.setter = setter;
            this.creator = creator;
            this.dataPoints = dataPoints;
            Model = CreatePlotModel();
        }

        public void InvalidateGraphs()
        {
            currentSerie.Points.Clear();

            foreach (var point in dataPoints)
                currentSerie.Points.Add(new DataPoint(point.Time, getter(point)));

            Model.InvalidatePlot(true);
            Model.ResetAllAxes();
        }

        private PlotModel CreatePlotModel()
        {
            var model = new PlotModel();

            // Y-axis (value)
            model.Axes.Add(new LinearAxis
            {
                MajorGridlineStyle = LineStyle.Solid,
                MinorGridlineStyle = LineStyle.Solid,
            });

            // X-axis (time)
            model.Axes.Add(new LinearAxis
            {
                MajorGridlineStyle = LineStyle.Solid,
                MinorGridlineStyle = LineStyle.Solid,
                Position = AxisPosition.Bottom,
            });

            currentSerie = new LineSeries
            {
                Color = OxyColors.DarkTurquoise,
                MarkerType = MarkerType.Triangle,
                MarkerSize = 6,
                MarkerStroke = OxyColors.Teal,
                MarkerFill = OxyColors.SkyBlue,
                MarkerStrokeThickness = 1
            };

            foreach (var point in dataPoints)
                currentSerie.Points.Add(new DataPoint(point.Time, getter(point)));

            model.Series.Add(currentSerie);
            currentSerie.MouseDown += MouseDownHandler;
            currentSerie.MouseMove += MouseMoveHandler;
            currentSerie.MouseUp += MouseUpHandler;
            return model;
        }

        private void MouseUpHandler(object sender, OxyMouseEventArgs e)
        {
            indexOfPointToMove = -1;
            Model.InvalidatePlot(true);
            Model.ResetAllAxes();
            e.Handled = true;

            for (int i = 0; i < currentSerie.Points.Count; ++i)
            {
                dataPoints[i].Time = currentSerie.Points[i].X;
                setter(dataPoints[i], currentSerie.Points[i].Y);
            }

            PointsUpdated?.Invoke(this, new EventArgs());
        }

        private void MouseMoveHandler(object sender, OxyMouseEventArgs e)
        {
            if (indexOfPointToMove == -1)
                return;

            DataPoint point = currentSerie.InverseTransform(e.Position);

            if (indexOfPointToMove == 0)
            {
                // first point to check, it's X position is always 0.0
                double newY = point.Y < 0 ? 0.0 : point.Y;
                point = new DataPoint(0.0, newY);
            }
            else if (indexOfPointToMove == currentSerie.Points.Count - 1)
            {
                // last point to check, it's Y position is always signal-max-time
                double newY = point.Y < 0 ? 0.0 : point.Y;
                point = new DataPoint(dataPoints[dataPoints.Count - 1].Time, newY);
            }
            else
            {
                // any other point between first and last
                DataPoint prev = currentSerie.Points[indexOfPointToMove - 1];
                DataPoint next = currentSerie.Points[indexOfPointToMove + 1];

                double newX = point.X < prev.X ? prev.X : (point.X > next.X ? next.X : point.X);
                double newY = point.Y < 0 ? 0.0 : point.Y;
                point = new DataPoint(newX, newY);
            }

            currentSerie.Points[indexOfPointToMove] = point;
            Model.InvalidatePlot(false);
            e.Handled = true;
        }

        private void MouseDownHandler(object sender, OxyMouseDownEventArgs e)
        {
            if (e.ChangedButton != OxyMouseButton.Left)
                return;

            int indexOfNearestPoint = (int)Math.Round(e.HitTestResult.Index);
            var nearestPoint = currentSerie.Transform(currentSerie.Points[indexOfNearestPoint]);

            if ((nearestPoint - e.Position).Length < 10)
            {
                // move current selected point
                indexOfPointToMove = indexOfNearestPoint;
            }
            else
            {
                // create new point and move it instead
                int i = (int)e.HitTestResult.Index + 1;

                T newPoint = creator(e.Position.X) as T;
                setter(newPoint, e.Position.Y);
                dataPoints.Insert(i, newPoint);

                currentSerie.Points.Insert(i, currentSerie.InverseTransform(e.Position));
                indexOfPointToMove = i;
            }

            Model.InvalidatePlot(false);
            e.Handled = true;
        }
    }
}
