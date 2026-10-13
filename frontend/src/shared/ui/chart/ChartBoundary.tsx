import { Component, type ReactNode } from 'react';

interface ChartBoundaryProps {
  children: ReactNode;
  /** Shown instead of the chart when it failed to load (the library chunk could not be fetched) or to draw. */
  fallback: ReactNode;
  onError: () => void;
}

/** Keeps a failed chart from taking the page down: the rest of the card, and the data table, stay usable. */
export class ChartBoundary extends Component<ChartBoundaryProps, { failed: boolean }> {
  override state = { failed: false };

  static getDerivedStateFromError() {
    return { failed: true };
  }

  override componentDidCatch() {
    this.props.onError();
  }

  override render() {
    return this.state.failed ? this.props.fallback : this.props.children;
  }
}
