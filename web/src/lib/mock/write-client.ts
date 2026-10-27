import { toast } from "sonner";
import type {
  CreatedSubject,
  LifeOsWriteClient,
  NewSubjectRequest,
} from "@/lib/production-ui-types";
import { inferChildRelation, renamedMessage } from "@/lib/production-ui-types";
import { extensionForMime, sha256Hex } from "@/lib/artifacts";
import { redemptionMessage } from "@/lib/rewards";
import { foodWriteMessage } from "@/lib/food";
import { exceptionMarkedMessage, exceptionUnmarkedMessage } from "@/lib/domains";
import {
  addEdge,
  earnDay,
  earnStatus,
  mockHomeWrite,
  mockSettleDomains,
  mockMarkException,
  mockUnmarkException,
  mockCloseFoodDay,
  mockDeleteFood,
  mockEditFood,
  mockFastAction,
  mockLogFood,
  mockRemoveFoodPhoto,
  mockSetFoodPhoto,
  mockSettleActivity,
  mockSettleFood,
  appendJournal,
  applyTimer,
  applyAttrs,
  archiveSubject,
  byRef,
  captureDocument,
  captureNote,
  captureVoice,
  completeReviewDoc,
  createSubject,
  dropInboxItem,
  flagItem,
  involvePerson,
  mockAddReward,
  mockPriceReward,
  mockRedeem,
  mockClearCover,
  mockMoveCover,
  mockSetBounty,
  mockSetCover,
  mockSetHabitTarget,
  mockTargetReward,
  relateEvent,
  removeInbox,
  renameSubject,
  renameTag,
  saveReviewBody,
  setRecurrence,
  mockSettleTasks,
  setTags,
} from "@/lib/mock/store";
import { getState } from "@/lib/mock/store";
import {
  mockAuthorizeOura,
  mockConnectOura,
  mockSetWorkoutNote,
  mockSyncActivity,
} from "@/lib/mock/activity-store";
import {
  mockAddSpace,
  mockArchiveSpace,
  mockDeleteSpaceRating,
  mockEditSpaceRating,
  mockRateSpaces,
  mockSetSpace,
} from "@/lib/mock/environment-store";
import { ratedMessage } from "@/lib/environment";

const wait = (ms = 90) => new Promise((r) => setTimeout(r, ms));

function notify(message: string) {
  toast.success(message);
}

export function createMockWriteClient(onChange?: () => void): LifeOsWriteClient {
  const bump = () => onChange?.();

  return {
    async newSubject(req: NewSubjectRequest): Promise<CreatedSubject> {
      await wait();
      let relation = req.relation;
      if (req.parent && !relation) {
        const parent = byRef(req.parent);
        if (parent) relation = inferChildRelation(req.type, parent.type);
      }
      const created = createSubject({ ...req, relation });
      notify(`Created ${created.title}`);
      bump();
      return created;
    },

    async promote(source, type, title) {
      await wait();
      const created = createSubject({ type, title });
      // Promotion carries the source's tags onto the new subject (as the kernel does).
      const carried = getState().tagsByItem[byRef(source)?.id ?? source] ?? [];
      if (carried.length) setTags(created.id, carried);
      removeInbox(source, "promoted");
      notify(`Promoted to ${type}`);
      bump();
      return created;
    },

    async setAttributes(subject, attrs) {
      await wait();
      applyAttrs(byRef(subject)?.id ?? subject, attrs);
      notify("Saved");
      bump();
    },

    async rename(subject, title) {
      await wait();
      const renamed = renameSubject(subject, title);
      if (renamed.changed) notify(renamedMessage(renamed));
      bump();
      return renamed;
    },

    async setStatus(subject, status) {
      await wait();
      const earned = earnStatus(subject, status);
      notify(`Status → ${status}`);
      bump();
      return earned;
    },

    async archive(subject) {
      await wait();
      // A space is archived in the home's half of the store (HOME-1).
      if (!mockArchiveSpace(subject, true)) archiveSubject(subject, true);
      notify("Archived");
      bump();
    },

    async restore(subject) {
      await wait();
      if (!mockArchiveSpace(subject, false)) archiveSubject(subject, false);
      notify("Restored");
      bump();
    },

    async tag(item, opts) {
      await wait();
      setTags(item, opts.add, opts.remove);
      bump();
    },

    async renameTag(from, to) {
      await wait();
      const renamed = renameTag(from, to);
      if (renamed.renamed + renamed.merged > 0) notify(`Renamed #${renamed.from} → #${renamed.to}`);
      bump();
      return renamed;
    },

    async link(from, relation, to) {
      await wait();
      const a = byRef(from);
      const b = byRef(to);
      if (a && b) addEdge(a.id, relation, b.id);
      notify("Linked");
      bump();
    },

    async relate(eventId, subject, as) {
      await wait();
      relateEvent(eventId, subject, as);
      const target = byRef(subject);
      notify(target ? `Related to ${target.title}` : "Related");
      bump();
    },

    async flag(item) {
      await wait();
      flagItem(item);
      notify("Flagged to Inbox");
      bump();
    },

    async dismiss(item) {
      await wait();
      removeInbox(item);
      // No toast here: the Inbox shows "Dismissed" with an Undo action.
      bump();
    },

    async drop(item) {
      await wait();
      dropInboxItem(item);
      notify("Dropped");
      bump();
    },

    async adhere(habit, state, opts) {
      await wait();
      const earned = earnDay(habit, state, opts?.on, opts?.note);
      notify(
        state === "followed"
          ? "Marked followed"
          : state === "partial"
            ? "Partial credit"
            : "Marked missed",
      );
      bump();
      return earned;
    },

    async settleDomains() {
      await wait();
      const earned = mockSettleDomains();
      if (earned.awards.length > 0) bump();
      return earned;
    },

    async markException(comment, day) {
      await wait();
      const result = mockMarkException(comment, day);
      notify(exceptionMarkedMessage(result.exception?.day ?? day));
      bump();
      return result;
    },

    async unmarkException(day) {
      await wait();
      const result = mockUnmarkException(day);
      notify(exceptionUnmarkedMessage(day));
      bump();
      return result;
    },

    async logFood(req) {
      await wait();
      const result = mockLogFood(req);
      notify(foodWriteMessage(result));
      bump();
      return result;
    },

    async editFood(meal, req) {
      await wait();
      const result = mockEditFood(meal, req);
      notify(foodWriteMessage(result));
      bump();
      return result;
    },

    async setFoodPhoto(meal, photo, filename) {
      await wait();
      const result = mockSetFoodPhoto(meal, photo, filename);
      notify(foodWriteMessage(result));
      bump();
      return result;
    },

    async removeFoodPhoto(meal) {
      await wait();
      const result = mockRemoveFoodPhoto(meal);
      notify(foodWriteMessage(result));
      bump();
      return result;
    },

    async deleteFood(meal) {
      await wait();
      const result = mockDeleteFood(meal);
      notify(foodWriteMessage(result));
      bump();
      return result;
    },

    async fast(req) {
      await wait();
      const result = mockFastAction(req);
      notify(foodWriteMessage(result));
      bump();
      return result;
    },

    async closeFoodDay(day) {
      await wait();
      const result = mockCloseFoodDay(day);
      notify(foodWriteMessage(result));
      bump();
      return result;
    },

    async settleFood() {
      await wait();
      const earned = mockSettleFood();
      if (earned.awards.length > 0) bump();
      return earned;
    },

    async authorizeOura(redirectUri) {
      await wait();
      return mockAuthorizeOura(redirectUri);
    },

    async connectOura(code, state) {
      await wait();
      const status = mockConnectOura(code, state);
      bump();
      return status;
    },

    async syncActivity(force) {
      await wait();
      const result = mockSyncActivity(force);
      // Like the API, it settles activity XP even when the sync itself is skipped.
      const earned = mockSettleActivity();
      if (result.ran || earned.awards.length > 0) bump();
      return { ...result, ...earned };
    },

    async setWorkoutNote(workout, text) {
      await wait();
      const row = mockSetWorkoutNote(workout, text);
      bump();
      return row;
    },

    async addSpace(req) {
      await wait();
      const row = mockAddSpace(req);
      notify(`Added ${row.name}`);
      bump();
      return row;
    },

    async setSpace(space, req) {
      await wait();
      const row = mockSetSpace(space, req);
      notify(`Saved ${row.name}`);
      bump();
      return row;
    },

    async rateSpaces(ratings, day) {
      await wait();
      const result = mockHomeWrite(() => mockRateSpaces(ratings, day));
      notify(ratedMessage(result));
      bump();
      return result;
    },

    async editSpaceRating(rating, req) {
      await wait();
      const result = mockHomeWrite(() => mockEditSpaceRating(rating, req));
      notify("Rating saved");
      bump();
      return result;
    },

    async deleteSpaceRating(rating) {
      await wait();
      const result = mockHomeWrite(() => mockDeleteSpaceRating(rating));
      notify("Rating deleted");
      bump();
      return result;
    },

    async recur(subject, spec, opts) {
      await wait();
      setRecurrence(subject, spec, opts?.start);
      bump();
    },

    async settleTasks() {
      await wait();
      const result = mockSettleTasks();
      if (result.made.length > 0) bump();
      return result;
    },

    async timer(task, action) {
      await wait();
      const result = applyTimer(task, action);
      bump();
      return result;
    },

    async involve(subject, person, role, remove) {
      await wait();
      involvePerson(subject, person, role, remove);
      bump();
    },

    async appendJournal(subject, text) {
      await wait();
      appendJournal(subject, text);
      notify("Journal appended");
      bump();
    },

    async capture(text) {
      await wait();
      captureNote(text);
      notify("Captured to Inbox");
      bump();
    },

    async captureDocument(req) {
      await wait();
      // TODO(api): multipart upload to the kernel's attach write path (`bsk attach`).
      const sha256 = (await sha256Hex(req.file)) ?? null;
      const result = captureDocument({ ...req, sha256 });
      notify("Captured to Inbox");
      bump();
      return result;
    },

    async captureVoice(req) {
      await wait();
      // TODO(api): multipart upload to the kernel's voice write path (`bsk voice`).
      const keep = req.keepAudio || !req.transcript.trim();
      const sha256 = keep ? (await sha256Hex(req.audioBlob)) ?? null : null;
      const ext = extensionForMime(req.audioBlob.type || "audio/webm");
      const result = captureVoice({
        ...req,
        sha256,
        contentType: req.audioBlob.type || "audio/webm",
        filename: `voice.${ext}`,
      });
      notify("Captured to Inbox");
      bump();
      return result;
    },

    async saveReview(id, body) {
      await wait();
      saveReviewBody(id, body);
      notify("Review saved");
      bump();
    },

    async completeReview(id) {
      await wait();
      completeReviewDoc(id);
      notify("Review complete");
      bump();
    },

    async redeem(req) {
      await wait();
      const redemption = mockRedeem(req);
      notify(redemptionMessage(redemption));
      bump();
      return redemption;
    },

    async addReward(req) {
      await wait();
      const reward = mockAddReward(req);
      notify(`Added ${reward.name}`);
      bump();
      return reward;
    },

    async priceReward(reward, price) {
      await wait();
      const row = mockPriceReward(reward, price);
      notify("Price saved");
      bump();
      return row;
    },

    async targetReward(reward, targetUsd) {
      await wait();
      const row = mockTargetReward(reward, targetUsd);
      notify(targetUsd === null ? "Target removed" : "Target saved");
      bump();
      return row;
    },

    async setCover(req) {
      await wait();
      const result = mockSetCover(req);
      notify("Cover set");
      bump();
      return result;
    },

    async moveCover(subject, positionY) {
      await wait();
      const result = mockMoveCover(subject, positionY);
      notify("Cover moved");
      bump();
      return result;
    },

    async clearCover(subject) {
      await wait();
      const result = mockClearCover(subject);
      notify(result.removed ? "Cover removed" : "No cover to remove");
      bump();
      return result;
    },

    async setBounty(req) {
      await wait();
      const result = mockSetBounty(req);
      notify("Bounty saved");
      bump();
      return result;
    },

    async setHabitTarget(req) {
      await wait();
      const result = mockSetHabitTarget(req);
      notify(req.clear ? "Target back on the default" : "Target saved");
      bump();
      return result;
    },
  };
}
