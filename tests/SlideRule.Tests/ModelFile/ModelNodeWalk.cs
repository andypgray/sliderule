using System.Collections;
using System.Reflection;
using SlideRule.Fluent;
using SlideRule.Hosting;
using SlideRule.Model;

namespace SlideRule.Tests.ModelFile;

/// <summary>
///     Everything one rule reaches — every node object, every type anchor with the node and property it
///     hangs off, and whether anything on the way holds a predicate — walked by reflection over the model
///     itself rather than by asking the writer.
/// </summary>
/// <remarks>
///     <para>
///         Independent on purpose. The claim the exactness gate makes is that the writer's partition of a
///         model into carried and omitted rules is the one the model's own shape dictates, and a walk that
///         consulted the writer's registries could only ever agree with itself.
///     </para>
///     <para>
///         The spine is stated rather than reflected — constraint subject, operands, member operands,
///         member and project subjects, union parts, adjectives, a noun's layer definition, a family's
///         cells, the scope's scoped selection, the rule's exemplar — because a selection's noun is not always
///         readable (a union has none and says so by throwing) and because the member projections a selection offers
///         mint a fresh object on every read, which reflection would follow forever. Everything hanging off
///         a noun, an adjective, a member or a constraint is reflected, so a node class that grows a slot
///         is walked the day it does.
///     </para>
/// </remarks>
internal static class ModelNodeWalk
{
    // The base types a property has to be able to hold for this walk to bother reading it. Anything else
    // is a name, a glob, a flag or a back-pointer, and reading one costs a sentence render for nothing.
    private static readonly Type[] NodeBases =
    [
        typeof(TypeAnchor), typeof(Selection), typeof(MemberSelection), typeof(ProjectSelection),
        typeof(SelectionNoun), typeof(SelectionAdjective), typeof(MemberAdjective), typeof(ProjectAdjective),
        typeof(Constraint), typeof(Member)
    ];

    /// <summary>Everything <paramref name="rule" /> reaches.</summary>
    internal static Walked From(ArchRule rule)
    {
        var walk = new Walker();
        if (rule.Constraint is { } constraint) walk.Visit(constraint);
        if (rule.Scope is { } scope) walk.Visit(scope.Scoped);
        if (rule.ExemplarSelection is { } exemplar) walk.Visit(exemplar);

        return new Walked(walk.Nodes, walk.Anchors, walk.HoldsAPredicate);
    }

    /// <summary>An anchor, and the node and property it was found on — which is what decides its position.</summary>
    internal sealed record WalkedAnchor(Type Owner, string Property, TypeAnchor Anchor);

    /// <summary>What one walk found.</summary>
    internal sealed record Walked(
        IReadOnlyList<object> Nodes,
        IReadOnlyList<WalkedAnchor> Anchors,
        bool HoldsAPredicate);

    private sealed class Walker
    {
        private readonly HashSet<object> _visited = new(ReferenceEqualityComparer.Instance);

        internal List<object> Nodes { get; } = [];

        internal List<WalkedAnchor> Anchors { get; } = [];

        internal bool HoldsAPredicate { get; private set; }

        internal void Visit(object node)
        {
            if (!_visited.Add(node)) return;

            Nodes.Add(node);
            NoteAnyPredicate(node.GetType());
            Spine(node);
            ReadSlots(node);
        }

        // The structural parents, stated. A union has no noun to read, so it is asked for its parts
        // instead — the one place in the model where the shape decides which question is legal.
        private void Spine(object node)
        {
            switch (node)
            {
                case Constraint constraint:
                    if (constraint.Subject is { } subject) Visit(subject);
                    foreach (Selection operand in constraint.Operands) Visit(operand);
                    foreach (Member member in constraint.MemberOperands) Visit(member);
                    if (constraint is MemberConstraint memberConstraint) Visit(memberConstraint.MemberSubject);
                    if (constraint is ProjectConstraint projectConstraint) Visit(projectConstraint.ProjectSubject);
                    break;
                case UnionSelection union:
                    foreach (Selection part in union.Parts) Visit(part);
                    foreach (SelectionAdjective adjective in union.Adjectives) Visit(adjective);
                    break;
                case Selection selection:
                    Visit(selection.Noun);
                    foreach (SelectionAdjective adjective in selection.Adjectives) Visit(adjective);
                    break;
                case MemberSelection members:
                    Visit(members.Source);
                    foreach (MemberAdjective adjective in members.Adjectives) Visit(adjective);
                    break;
                case ProjectSelection projects:
                    foreach (ProjectAdjective adjective in projects.Adjectives) Visit(adjective);
                    break;
            }
        }

        // Whether this class holds a predicate at all — asked of the type, never of the instance, so it
        // stays true of a node whose other slots could not be read.
        private void NoteAnyPredicate(Type nodeType)
        {
            foreach (PropertyInfo property in Slots(nodeType))
                if (typeof(Delegate).IsAssignableFrom(property.PropertyType))
                    HoldsAPredicate = true;
        }

        private void ReadSlots(object node)
        {
            foreach (PropertyInfo property in Slots(node.GetType()))
            {
                if (!MayCarryNodes(property.PropertyType)) continue;

                object? value = property.GetValue(node);
                if (value is null) continue;

                Take(node.GetType(), property.Name, value);
                if (value is IEnumerable sequence and not string)
                    foreach (object? element in sequence)
                        if (element is not null)
                            Take(node.GetType(), property.Name, element);
            }
        }

        private void Take(Type owner, string property, object value)
        {
            if (value is TypeAnchor anchor)
            {
                Anchors.Add(new WalkedAnchor(owner, property, anchor));
                return;
            }

            if (NodeBases.Any(node => node.IsInstanceOfType(value))) Visit(value);
        }

        // Every declared slot up the chain, minus the ones the spine owns. `Selection`'s own are skipped
        // whole: its member projections mint a new selection on each read, and its noun is the question a
        // union refuses. A `Noun` override is skipped for the same reason.
        private static IEnumerable<PropertyInfo> Slots(Type nodeType)
        {
            for (Type? type = nodeType; type is not null && type != typeof(object); type = type.BaseType)
            {
                if (type == typeof(Selection)) continue;

                PropertyInfo[] declared = type.GetProperties(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                foreach (PropertyInfo property in declared)
                {
                    if (property.GetIndexParameters()
                            .Length > 0) continue;
                    if (property.Name == nameof(Selection.Noun)) continue;

                    yield return property;
                }
            }
        }

        private static bool MayCarryNodes(Type type)
        {
            if (type == typeof(string)) return false;
            if (NodeBases.Any(node => node.IsAssignableFrom(type))) return true;

            return ElementOf(type) is { } element && NodeBases.Any(node => node.IsAssignableFrom(element));
        }

        private static Type? ElementOf(Type type)
        {
            return type.GetInterfaces()
                .Concat([type])
                .Where(candidate => candidate.IsGenericType
                                    && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                .Select(candidate => candidate.GetGenericArguments()[0])
                .FirstOrDefault();
        }
    }
}
