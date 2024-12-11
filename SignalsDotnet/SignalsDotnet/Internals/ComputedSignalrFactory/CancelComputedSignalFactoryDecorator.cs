﻿using System.Reactive.Concurrency;
using System.Reactive.Linq;
using SignalsDotnet.Configuration;
using SignalsDotnet.Helpers;

namespace SignalsDotnet.Internals.ComputedSignalrFactory;

internal class CancelComputedSignalFactoryDecorator : IComputedSignalFactory
{
    readonly IComputedSignalFactory _parent;
    readonly IReadOnlySignal<CancellationToken> _cancellationSignal;

    public CancelComputedSignalFactoryDecorator(IComputedSignalFactory parent, IReadOnlySignal<CancellationToken> cancellationSignal)
    {
        _parent = parent;
        _cancellationSignal = cancellationSignal;
    }

    public IReadOnlySignal<T> Computed<T>(Func<T> func, Func<Optional<T>> fallbackValue, ReadonlySignalConfigurationDelegate<T?>? configuration = null)
    {
        return ComputedObservable(func, fallbackValue).ToSignal(configuration!)!;
    }

    public IReadOnlySignal<T> AsyncComputed<T>(Func<CancellationToken, ValueTask<T>> func,
                                               T startValue,
                                               Func<Optional<T>> fallbackValue,
                                               ConcurrentChangeStrategy concurrentChangeStrategy = default, ReadonlySignalConfigurationDelegate<T>? configuration = null)
    {
        return AsyncComputedObservable(func, startValue, fallbackValue, concurrentChangeStrategy).ToSignal(configuration!)!;
    }

    public IObservable<T> ComputedObservable<T>(Func<T> func, Func<Optional<T>> fallbackValue)
    {
        return _parent.ComputedObservable(() =>
                      {
                          if (_cancellationSignal.Value.IsCancellationRequested)
                          {
                              return Optional<T>.Empty;
                          }

                          return new Optional<T>(func());
                      }, () => new Optional<Optional<T>>(fallbackValue()))
                      .Where(x => x.HasValue)
                      .Select(x => x.Value!);
    }

    public IObservable<T> AsyncComputedObservable<T>(Func<CancellationToken, ValueTask<T>> func, T startValue, Func<Optional<T>> fallbackValue, ConcurrentChangeStrategy concurrentChangeStrategy = default)
    {
        return _parent.AsyncComputedObservable(async token =>
                      {
                          if (_cancellationSignal.Value.IsCancellationRequested || token.IsCancellationRequested)
                          {
                              return Optional<T>.Empty;
                          }

                          using var cts = CancellationTokenSource.CreateLinkedTokenSource(token, _cancellationSignal.UntrackedValue);
                          var result = await func(cts.Token);
                          return new Optional<T>(result);
                      }, new Optional<T>(startValue), () => new Optional<Optional<T>>(fallbackValue()), concurrentChangeStrategy)
                      .Where(static x => x.HasValue)
                      .Select(static x => x.Value)!;
    }

    public Effect Effect(Action onChange, IScheduler? scheduler)
    {
        return new Effect(() =>
        {
            if (_cancellationSignal.Value.IsCancellationRequested)
            {
                return;
            }

            onChange();
        }, scheduler);
    }

    public Effect AsyncEffect(Func<CancellationToken, ValueTask> onChange, ConcurrentChangeStrategy concurrentChangeStrategy, IScheduler? scheduler)
    {
        return new Effect(async token =>
        {
            if (_cancellationSignal.Value.IsCancellationRequested || token.IsCancellationRequested)
            {
                return;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationSignal.UntrackedValue, token);
            await onChange(cts.Token);
        }, concurrentChangeStrategy, scheduler);
    }
}