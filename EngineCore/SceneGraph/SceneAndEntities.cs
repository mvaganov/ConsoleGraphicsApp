#nullable enable
using System;
using System.Collections.Generic;
using ConsoleGraphicsEngine.MathCore;
using ConsoleGraphicsEngine.Physics;
using ConsoleGraphicsEngine.Rendering;

namespace ConsoleGraphicsEngine.SceneGraph {
	public abstract class Component {
		public Entity Entity { get; internal set; } = null!;
		public bool IsEnabled { get; set; } = true;
		public TransformComponent Transform => Entity.Transform;

		public virtual void OnAttach() { }
		public virtual void OnUpdate(float dt, long currentFrame) { }
		public virtual void OnDraw(IRenderBackend backend) { }
	}

	public sealed class TransformComponent : Component {
		public FVec2 Position { get; set; }
		public FNum Angle { get; set; }
		public Vec2 Scale { get; set; } = new Vec2(1f, 1f);

		public Vec2 WorldPositionFloat => Position.ToVec2();
		public float AngleFloat => Angle.ToFloat();
	}

	/// <summary>Two-way transform bridge between <see cref="Entity"/> and <see cref="Physics.Body"/>.</summary>
	public sealed class RigidBodyComponent : Component {
		public Body Body { get; }
		public RigidBodyComponent(Body body) { Body = body; }

		public override void OnAttach() => SyncFromPhysics();

		public void SyncFromPhysics() {
			Transform.Position = Body.Position;
			Transform.Angle = Body.Angle;
		}

		public void SyncToPhysics() {
			Body.Position = Transform.Position;
			Body.Angle = Transform.Angle;
			if (Body is PolygonBody poly) poly.UpdateWorld();
		}
	}

	public sealed class ShapeRendererComponent : Component {
		public Body ShapeBody { get; }
		public bool DrawSpoke { get; set; } = true;
		public bool DrawHighlight { get; set; }
		public bool DrawPositionLabel { get; set; }

		public ShapeRendererComponent(Body shapeBody) { ShapeBody = shapeBody; }

		public override void OnDraw(IRenderBackend g) {
			if (ShapeBody is PolygonBody poly) {
				g.FillPolygon(poly.RenderWorld, poly.Color);
				if (DrawSpoke && poly.RenderWorld.Length > 0) {
					g.DrawLine(poly.Position.ToVec2(), poly.RenderWorld[0], 1.1f, Rgb.Lerp(poly.Color, new Rgb(10, 15, 30), 0.65f));
				}
			} else if (ShapeBody is CircleBody circle) {
				Vec2 cPos = circle.Position.ToVec2();
				float cRad = circle.Radius.ToFloat();
				g.FillCircle(cPos, cRad, circle.Color);
				if (DrawHighlight) {
					g.FillCircle(cPos + new Vec2(-cRad * 0.3f, cRad * 0.3f), cRad * 0.35f, new Rgb(255, 225, 150));
				}
				if (DrawPositionLabel) {
					g.DrawText(cPos, FormatPoint(cPos), TextAlign.Center, new Rgb(10, 12, 22), 0.45f);
				}
			}
		}

		static string FormatPoint(Vec2 p) {
			double x = Math.Round(p.X, 2) == 0 ? 0.0 : p.X;
			double y = Math.Round(p.Y, 2) == 0 ? 0.0 : p.Y;
			return string.Format(System.Globalization.CultureInfo.InvariantCulture, "({0:F2}, {1:F2})", x, y);
		}
	}

	public sealed class Entity {
		public string Name { get; set; }
		public string Tag { get; set; } = "Untagged";
		public bool IsActive { get; set; } = true;
		public TransformComponent Transform { get; }
		readonly List<Component> _components = new List<Component>();
		public IReadOnlyList<Component> Components => _components;

		public Entity(string name, Vec2 initialPosition = default, float initialAngle = 0f) {
			Name = name;
			Transform = new TransformComponent {
				Position = FVec2.FromVec2(initialPosition),
				Angle = FNum.FromFloat(initialAngle)
			};
			AddComponent(Transform);
		}

		public T AddComponent<T>(T component) where T : Component {
			component.Entity = this;
			_components.Add(component);
			component.OnAttach();
			return component;
		}

		public T? GetComponent<T>() where T : Component {
			for (int i = 0; i < _components.Count; i++) {
				if (_components[i] is T match) return match;
			}
			return null;
		}

		public void Update(float dt, long currentFrame) {
			if (!IsActive) return;
			for (int i = 0; i < _components.Count; i++) {
				Component c = _components[i];
				if (c.IsEnabled) c.OnUpdate(dt, currentFrame);
			}
		}

		public void Draw(IRenderBackend backend) {
			if (!IsActive) return;
			for (int i = 0; i < _components.Count; i++) {
				Component c = _components[i];
				if (c.IsEnabled) c.OnDraw(backend);
			}
		}
	}

	public sealed class Scene {
		public string Name { get; }
		readonly List<Entity> _entities = new List<Entity>();
		public IReadOnlyList<Entity> Entities => _entities;

		public Scene(string name) { Name = name; }

		public Entity CreateEntity(string name, Vec2 position = default, float angle = 0f) {
			var entity = new Entity(name, position, angle);
			_entities.Add(entity);
			return entity;
		}

		public Entity? FindEntity(string name) {
			for (int i = 0; i < _entities.Count; i++) {
				if (string.Equals(_entities[i].Name, name, StringComparison.OrdinalIgnoreCase)) return _entities[i];
			}
			return null;
		}

		public void SyncTransformsFromPhysics() {
			for (int i = 0; i < _entities.Count; i++) {
				if (_entities[i].IsActive) _entities[i].GetComponent<RigidBodyComponent>()?.SyncFromPhysics();
			}
		}

		public void Update(float dt, long currentFrame) {
			for (int i = 0; i < _entities.Count; i++) _entities[i].Update(dt, currentFrame);
		}

		public void Draw(IRenderBackend backend) {
			for (int i = 0; i < _entities.Count; i++) _entities[i].Draw(backend);
		}
	}
}
