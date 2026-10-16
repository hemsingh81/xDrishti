using XDrishti.Strategies.Syntax;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Syntax;

/// <summary>The printer is canonical and round-trips: for generated trees, <c>Parse(Print(tree))</c> equals the tree and the printed text is a fixed point; however the same tree is laid out, it prints the same.</summary>
public sealed class ExpressionPrinterRoundTripTests
{
    private const int Trees = 1000;

    private static IEnumerable<(int Seed, Expression Tree)> Generated()
    {
        for (var seed = 1; seed <= Trees; seed++)
        {
            yield return (seed, new AstGenerator(seed).Expression(1 + (seed % 6)));
        }
    }

    [Fact]
    public void Parsing_the_printed_text_of_a_tree_gives_the_tree_back()
    {
        foreach (var (seed, tree) in Generated())
        {
            var text = ExpressionPrinter.Print(tree);

            var result = ConditionParser.Parse(text);

            result.Problems.ShouldBeEmpty($"seed {seed}: {text}");
            result.Tree.ShouldBe(tree, $"seed {seed}: {text}");
        }
    }

    [Fact]
    public void The_printed_text_is_a_fixed_point_of_parse_and_print()
    {
        foreach (var (seed, tree) in Generated())
        {
            var text = ExpressionPrinter.Print(tree);

            ExpressionPrinter.Print(ConditionParser.Parse(text).Tree!).ShouldBe(text, $"seed {seed}");
        }
    }

    [Fact]
    public void However_a_tree_is_laid_out_it_prints_the_same_and_parses_to_the_same_tree()
    {
        var random = new Random(77);
        foreach (var (seed, tree) in Generated())
        {
            var canonical = ExpressionPrinter.Print(tree);
            for (var variant = 0; variant < 3; variant++)
            {
                var laid = ReferenceFormatter.Laid(tree, random);

                var result = ConditionParser.Parse(laid);

                result.Problems.ShouldBeEmpty($"seed {seed}: {laid}");
                result.Tree.ShouldBe(tree, $"seed {seed}: {laid}");
                ExpressionPrinter.Print(result.Tree!).ShouldBe(canonical, $"seed {seed}: {laid}");
            }
        }
    }

    [Fact]
    public void The_generator_covers_every_node_kind_every_operator_and_deep_trees()
    {
        var kinds = new HashSet<string>();
        var binaryOperators = new HashSet<BinaryOperator>();
        var unaryOperators = new HashSet<UnaryOperator>();
        var namedArguments = 0;
        var qualifiedCalls = 0;
        var scopedReferences = 0;
        var qualifiedReferences = 0;
        var negativeLags = 0;
        var deepest = 0;
        foreach (var (_, tree) in Generated())
        {
            deepest = Math.Max(deepest, DepthOf(tree));
            foreach (var node in Walk(tree))
            {
                kinds.Add(node.GetType().Name);
                switch (node)
                {
                    case Binary binary:
                        binaryOperators.Add(binary.Operator);
                        break;
                    case Unary unary:
                        unaryOperators.Add(unary.Operator);
                        break;
                    case Call call:
                        namedArguments += call.Arguments.Count(a => a is NamedArgument);
                        qualifiedCalls += call.Timeframe is null ? 0 : 1;
                        break;
                    case Reference reference:
                        scopedReferences += reference.Scope is null ? 0 : 1;
                        qualifiedReferences += reference.Timeframe is null ? 0 : 1;
                        break;
                    case Subscript { Lag: < 0 }:
                        negativeLags++;
                        break;
                }
            }
        }

        kinds.ShouldBe(["NumberLiteral", "StringLiteral", "BoolLiteral", "Reference", "Call", "NamedArgument", "Subscript", "Unary", "Binary"], ignoreOrder: true);
        binaryOperators.Count.ShouldBe(Enum.GetValues<BinaryOperator>().Length);
        unaryOperators.Count.ShouldBe(2);
        namedArguments.ShouldBeGreaterThan(50);
        qualifiedCalls.ShouldBeGreaterThan(50);
        scopedReferences.ShouldBeGreaterThan(100);
        qualifiedReferences.ShouldBeGreaterThan(100);
        negativeLags.ShouldBeGreaterThan(20);
        deepest.ShouldBeGreaterThanOrEqualTo(6);
        Generated().Count().ShouldBeGreaterThanOrEqualTo(500);
    }

    /// <summary>The depth of a tree, by recursion (the generated trees are shallow): the root alone is 0.</summary>
    private static int DepthOf(Expression node) => node switch
    {
        Binary binary => 1 + Math.Max(DepthOf(binary.Left), DepthOf(binary.Right)),
        Unary unary => 1 + DepthOf(unary.Operand),
        Subscript subscript => 1 + DepthOf(subscript.Target),
        Call call => 1 + call.Arguments.OfType<PositionalArgument>().Select(a => DepthOf(a.Value)).DefaultIfEmpty(-1).Max(),
        _ => 0,
    };

    private static IEnumerable<SyntaxNode> Walk(Expression root)
    {
        var pending = new Stack<SyntaxNode>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            yield return node;
            switch (node)
            {
                case Binary binary:
                    pending.Push(binary.Left);
                    pending.Push(binary.Right);
                    break;
                case Unary unary:
                    pending.Push(unary.Operand);
                    break;
                case Subscript subscript:
                    pending.Push(subscript.Target);
                    break;
                case Call call:
                    foreach (var argument in call.Arguments)
                    {
                        pending.Push(argument is PositionalArgument positional ? positional.Value : argument);
                    }

                    break;
            }
        }
    }
}
