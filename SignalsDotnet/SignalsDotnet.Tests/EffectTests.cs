﻿using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using FluentAssertions;

namespace SignalsDotnet.Tests;

public class EffectTests
{
    [Fact]
    public void ShouldRunWhenAnySignalChanges()
    {
        var number1 = new Signal<int>();
        var number2 = new Signal<int>();

        int sum = -1;
        var effect = new Effect(() => sum = number1.Value + number2.Value);
        sum.Should().Be(0);

        number1.Value = 1;
        sum.Should().Be(1);

        number1.Value = 2;
        sum.Should().Be(2);

        number2.Value = 2;
        sum.Should().Be(4);

        effect.Dispose();

        number2.Value = 3;
        sum.Should().Be(4);
    }

    [Fact]
    public void ShouldRunOnSpecifiedScheduler()
    {
        var scheduler = new TestScheduler();
        var number1 = new Signal<int>();
        var number2 = new Signal<int>();

        int sum = -1;
        var effect = new Effect(() => sum = number1.Value + number2.Value, scheduler);
        sum.Should().Be(0);

        number1.Value = 1;
        sum.Should().Be(0);

        number1.Value = 2;
        sum.Should().Be(0);

        scheduler.ExecuteAllPendingActions();
        sum.Should().Be(2);

        effect.Dispose();

        number2.Value = 3;
        scheduler.ExecuteAllPendingActions();
        sum.Should().Be(2);
    }

    [Fact]
    public void EffectShouldNotRunMultipleTimesInASingleSchedule()
    {
        var scheduler = new TestScheduler();
        var number1 = new Signal<int>();
        var number2 = new Signal<int>();

        int executionsCount = 0;
        var effect = new Effect(() =>
        {
            _ = number1.Value + number2.Value;
            executionsCount++;
        }, scheduler);
        executionsCount.Should().Be(1);

        number2.Value = 4;
        number2.Value = 3;

        number1.Value = 4;
        number1.Value = 3;
        executionsCount.Should().Be(1);

        scheduler.ExecuteAllPendingActions();
        executionsCount.Should().Be(2);

        effect.Dispose();

        number1.Value = 4;
        number1.Value = 3;
        scheduler.ExecuteAllPendingActions();
        executionsCount.Should().Be(2);
    }

    [Fact]
    public void EffectsShouldRunAtTheEndOfAtomicOperations()
    {
        Enumerable.Range(1,33)
                  .Select(__ => Observable.FromAsync(() => Task.Run(() =>
        {
            var number1 = new Signal<int>();
            var number2 = new Signal<int>();

            int sum = -1;
            _ = new Effect(() => sum = number1.Value + number2.Value);
            //sum.Should().Be(0);

            Effect.AtomicOperation(() =>
            {
                number1.Value = 1;
                sum.Should().Be(0);

                number1.Value = 2;
                sum.Should().Be(0);
            });
            sum.Should().Be(2);

            Effect.AtomicOperation(() =>
            {
                number2.Value = 2;
                sum.Should().Be(2);

                Effect.AtomicOperation(() =>
                {
                    number2.Value = 3;
                    sum.Should().Be(2);
                });

                sum.Should().Be(2);
            });

            sum.Should().Be(5);
        }))).Merge().ToTask().Wait();

    }

    [Fact]
    public void EffectsShouldRunAtTheEndOfAtomicOperationsWithScheduler()
    {
        var scheduler = new TestScheduler();
        var number1 = new Signal<int>();
        var number2 = new Signal<int>();

        int sum = -1;
        _ = new Effect(() => sum = number1.Value + number2.Value, scheduler);
        sum.Should().Be(0);

        Effect.AtomicOperation(() =>
        {
            number1.Value = 1;
            scheduler.ExecuteAllPendingActions();
            sum.Should().Be(0);

            number1.Value = 2;
            scheduler.ExecuteAllPendingActions();
            sum.Should().Be(0);
        });
        sum.Should().Be(0);

        scheduler.ExecuteAllPendingActions();
        sum.Should().Be(2);
    }
}

public class TestScheduler : IScheduler
{
    Action? _actions;
    public void ExecuteAllPendingActions()
    {
        var actions = _actions;
        _actions = null;
        actions?.Invoke();
    }

    public IDisposable Schedule<TState>(TState state, Func<IScheduler, TState, IDisposable> action)
    {
        _actions += () => action(this, state);
        return Disposable.Empty;
    }

    public IDisposable Schedule<TState>(TState state, TimeSpan dueTime, Func<IScheduler, TState, IDisposable> action) => Schedule(state, action);
    public IDisposable Schedule<TState>(TState state, DateTimeOffset dueTime, Func<IScheduler, TState, IDisposable> action) => Schedule(state, action);
    public DateTimeOffset Now => DateTimeOffset.UnixEpoch;
}