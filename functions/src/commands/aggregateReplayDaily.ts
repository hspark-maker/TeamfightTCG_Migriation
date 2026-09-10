import {onDocumentCreated} from "firebase-functions/v2/firestore";
import {DATABASE_ID} from "../firebaseApp";
import {consumeReplayDaily} from "../battleReplayTelemetry";
import {isKnownEnv} from "../save/environments";

export const aggregateReplayDaily = onDocumentCreated({
  document: "envs/{env}/replayTelemetryEvents/{eventId}", database: DATABASE_ID, retry: true,
}, async (event) => {
  if (!isKnownEnv(event.params.env) || !event.data) return;
  await consumeReplayDaily(event.params.env, event.data.ref);
});
