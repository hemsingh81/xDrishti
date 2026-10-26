import { HttpTransportType, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import type { HubTransport } from './eventsHub';
import {
  eventsHub as contract,
  type ExperimentJobFinished,
  type ExperimentJobProgress,
  type LabRunFinished,
  type LabRunProgress,
  type LiveFeedStatus,
  type LiveScope,
  type PricesChanged,
  type SubscribeAck,
} from './hubContract.generated';

/**
 * The real connection: SignalR over WebSockets only, negotiation skipped (the server accepts nothing else, and `negotiate`
 * would need the CSRF header). The session cookie travels with the upgrade request of the same origin. Nothing is logged:
 * the library's own log would print URLs.
 */
export function createSignalrTransport(): HubTransport {
  const connection = new HubConnectionBuilder()
    .withUrl(contract.route, { skipNegotiation: true, transport: HttpTransportType.WebSockets })
    .configureLogging(LogLevel.None)
    .build();
  return {
    start: () => connection.start(),
    stop: () => connection.stop(),
    subscribe: (scope: LiveScope) => connection.invoke<SubscribeAck>(contract.methods.subscribe, scope),
    unsubscribe: () => connection.invoke<undefined>(contract.methods.unsubscribe),
    onPricesChanged: (handler: (message: PricesChanged) => void) => {
      connection.on(contract.events.pricesChanged, handler);
    },
    onFeedStatus: (handler: (message: LiveFeedStatus) => void) => {
      connection.on(contract.events.feedStatus, handler);
    },
    onLabRunProgress: (handler: (message: LabRunProgress) => void) => {
      connection.on(contract.events.labRunProgress, handler);
    },
    onLabRunFinished: (handler: (message: LabRunFinished) => void) => {
      connection.on(contract.events.labRunFinished, handler);
    },
    onExperimentProgress: (handler: (message: ExperimentJobProgress) => void) => {
      connection.on(contract.events.experimentProgress, handler);
    },
    onExperimentFinished: (handler: (message: ExperimentJobFinished) => void) => {
      connection.on(contract.events.experimentFinished, handler);
    },
    onClose: (handler: () => void) => {
      connection.onclose(handler);
    },
  };
}
