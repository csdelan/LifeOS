import type {
  CreatedSubject,
  LifeOsWriteClient,
  NewSubjectRequest,
} from "@/lib/production-ui-types";
import { inferChildRelation } from "@/lib/production-ui-types";
import { extensionForMime, sha256Hex } from "@/lib/artifacts";
import {
  addEdge,
  removeEdge,
  earnDay,
  earnStatus,
  mockHomeWrite,
  mockSettleDomains,
  mockMarkException,
  mockUnmarkException,
  mockSettleActivity,
  appendJournal,
  applyTimer,
  applyAttrs,
  archiveSubject,
  byRef,
  captureDocument,
  captureNote,
  captureVoice,
  completeReviewDoc,
  openReviewDoc,
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
  materializeAppointments,
  setTags,
} from "@/lib/mock/store";
import {
  mockCloseFoodDay,
  mockDeleteFood,
  mockEditFood,
  mockFastAction,
  mockLogFood,
  mockRemoveFoodPhoto,
  mockSetFoodPhoto,
  mockSettleFood,
} from "@/lib/mock/food-store";
import { getState } from "@/lib/mock/store";
import {
  mockAuthorizeOura,
  mockConnectOura,
  mockSetWorkoutNote,
  mockSyncActivity,
} from "@/lib/mock/activity-store";
import {
  mockAddShot,
  mockAddSpace,
  mockAddSpacePhoto,
  mockArchiveSpace,
  mockChangeSpacePhoto,
  mockDeleteSpaceRating,
  mockEditSpaceRating,
  mockRateSpaces,
  mockRemoveShot,
  mockRemoveSpacePhoto,
  mockRenameShot,
  mockSetSpace,
  mockSetSpaceBaseline,
} from "@/lib/mock/environment-store";
import {
  mockAddMetric,
  mockArchiveMetric,
  mockAttachLabReport,
  mockDeleteLabVisit,
  mockLogLabVisit,
  mockRemoveLabReport,
  mockSaveLabVisit,
  mockDeleteReading,
  mockEditReading,
  mockLogReading,
  mockLogReadings,
  mockSetMetric,
} from "@/lib/mock/measurements-store";
import { mockRemovePersonPhoto, mockSetPersonPhoto } from "@/lib/mock/people-store";

const wait = (ms = 90) => new Promise((r) => setTimeout(r, ms));

/** The exception days a photo's score reads, from the main store. */
const exceptions = () => getState().exceptionDays;

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
      bump();
      return created;
    },

    async setAttributes(subject, attrs) {
      await wait();
      applyAttrs(byRef(subject)?.id ?? subject, attrs);
      bump();
    },

    async rename(subject, title) {
      await wait();
      const renamed = renameSubject(subject, title);
      bump();
      return renamed;
    },

    async setStatus(subject, status) {
      await wait();
      const earned = earnStatus(subject, status);
      bump();
      return earned;
    },

    async archive(subject) {
      await wait();
      // A space is archived in the home's half of the store (HOME-1), a metric in measurements'.
      if (!mockArchiveSpace(subject, true) && !mockArchiveMetric(subject, true)) archiveSubject(subject, true);
      bump();
    },

    async restore(subject) {
      await wait();
      if (!mockArchiveSpace(subject, false) && !mockArchiveMetric(subject, false)) archiveSubject(subject, false);
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
      bump();
      return renamed;
    },

    async link(from, relation, to, remove) {
      await wait();
      const a = byRef(from);
      const b = byRef(to);
      if (a && b) {
        if (remove) removeEdge(a.id, relation, b.id);
        else addEdge(a.id, relation, b.id);
      }
      bump();
    },

    async relate(eventId, subject, as) {
      await wait();
      relateEvent(eventId, subject, as);
      bump();
    },

    async flag(item) {
      await wait();
      flagItem(item);
      bump();
    },

    async dismiss(item) {
      await wait();
      removeInbox(item);
      bump();
    },

    async drop(item) {
      await wait();
      dropInboxItem(item);
      bump();
    },

    async adhere(habit, state, opts) {
      await wait();
      const earned = earnDay(habit, state, opts?.on, opts?.note);
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
      bump();
      return result;
    },

    async unmarkException(day) {
      await wait();
      const result = mockUnmarkException(day);
      bump();
      return result;
    },

    async logFood(req) {
      await wait();
      const result = mockLogFood(req);
      bump();
      return result;
    },

    async editFood(meal, req) {
      await wait();
      const result = mockEditFood(meal, req);
      bump();
      return result;
    },

    async setFoodPhoto(meal, photo, filename) {
      await wait();
      const result = mockSetFoodPhoto(meal, photo, filename);
      bump();
      return result;
    },

    async removeFoodPhoto(meal) {
      await wait();
      const result = mockRemoveFoodPhoto(meal);
      bump();
      return result;
    },

    async setPersonPhoto(person, photo, filename) {
      await wait();
      const result = mockSetPersonPhoto(person, photo, filename ?? (photo instanceof File ? photo.name : undefined));
      bump();
      return result;
    },

    async removePersonPhoto(person) {
      await wait();
      const result = mockRemovePersonPhoto(person);
      bump();
      return result;
    },

    async deleteFood(meal) {
      await wait();
      const result = mockDeleteFood(meal);
      bump();
      return result;
    },

    async fast(req) {
      await wait();
      const result = mockFastAction(req);
      bump();
      return result;
    },

    async closeFoodDay(day) {
      await wait();
      const result = mockCloseFoodDay(day);
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
      bump();
      return row;
    },

    async setSpace(space, req) {
      await wait();
      const row = mockSetSpace(space, req);
      bump();
      return row;
    },

    async rateSpaces(ratings, day) {
      await wait();
      const result = mockHomeWrite(() => mockRateSpaces(ratings, day));
      bump();
      return result;
    },

    async editSpaceRating(rating, req) {
      await wait();
      const result = mockHomeWrite(() => mockEditSpaceRating(rating, req));
      bump();
      return result;
    },

    async deleteSpaceRating(rating) {
      await wait();
      const result = mockHomeWrite(() => mockDeleteSpaceRating(rating));
      bump();
      return result;
    },

    async addSpacePhoto(space, photo, req) {
      await wait();
      const result = mockAddSpacePhoto(space, photo, req, exceptions());
      bump();
      return result;
    },

    async changeSpacePhoto(photo, req) {
      await wait();
      const result = mockChangeSpacePhoto(photo, req, exceptions());
      bump();
      return result;
    },

    async removeSpacePhoto(photo) {
      await wait();
      const result = mockRemoveSpacePhoto(photo, exceptions());
      bump();
      return result;
    },

    async setSpaceBaseline(photo) {
      await wait();
      const result = mockSetSpaceBaseline(photo, exceptions());
      bump();
      return result;
    },

    async addShot(space, name) {
      await wait();
      const result = mockAddShot(space, name, exceptions());
      bump();
      return result;
    },

    async renameShot(space, shot, name) {
      await wait();
      const result = mockRenameShot(space, shot, name, exceptions());
      bump();
      return result;
    },

    async removeShot(space, shot) {
      await wait();
      const result = mockRemoveShot(space, shot, exceptions());
      bump();
      return result;
    },

    async addMetric(req) {
      await wait();
      const row = mockAddMetric(req);
      bump();
      return row;
    },

    async setMetric(metric, req) {
      await wait();
      const row = mockSetMetric(metric, req);
      bump();
      return row;
    },

    async logReading(req) {
      await wait();
      const result = mockLogReading(req);
      bump();
      return result;
    },

    async logReadings(req) {
      await wait();
      const result = mockLogReadings(req);
      bump();
      return result;
    },

    async editReading(reading, req) {
      await wait();
      const result = mockEditReading(reading, req);
      bump();
      return result;
    },

    async deleteReading(reading) {
      await wait();
      const result = mockDeleteReading(reading);
      bump();
      return result;
    },

    async logLabVisit(req) {
      await wait();
      const result = mockLogLabVisit(req);
      bump();
      return result;
    },

    async saveLabVisit(visit, req) {
      await wait();
      const result = mockSaveLabVisit(visit, req);
      bump();
      return result;
    },

    async deleteLabVisit(visit) {
      await wait();
      const result = mockDeleteLabVisit(visit);
      bump();
      return result;
    },

    async attachLabReport(visit, report, filename) {
      await wait();
      const result = mockAttachLabReport(visit, report, filename ?? (report instanceof File ? report.name : undefined));
      bump();
      return result;
    },

    async removeLabReport(visit) {
      await wait();
      const result = mockRemoveLabReport(visit);
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

    async materialize(series, through) {
      await wait();
      const result = materializeAppointments(series, through);
      bump();
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
      bump();
    },

    async capture(text, type) {
      await wait();
      captureNote(text, type);
      bump();
    },

    async captureDocument(req) {
      await wait();
      // TODO(api): multipart upload to the kernel's attach write path (`bsk attach`).
      const sha256 = (await sha256Hex(req.file)) ?? null;
      const result = captureDocument({ ...req, sha256 });
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
      bump();
      return result;
    },

    async openReview(kind, date) {
      await wait();
      const doc = openReviewDoc(kind, date);
      bump();
      return doc;
    },

    async saveReview(id, body) {
      await wait();
      saveReviewBody(id, body);
      bump();
    },

    async completeReview(id) {
      await wait();
      completeReviewDoc(id);
      bump();
    },

    async redeem(req) {
      await wait();
      const redemption = mockRedeem(req);
      bump();
      return redemption;
    },

    async addReward(req) {
      await wait();
      const reward = mockAddReward(req);
      bump();
      return reward;
    },

    async priceReward(reward, price) {
      await wait();
      const row = mockPriceReward(reward, price);
      bump();
      return row;
    },

    async targetReward(reward, targetUsd) {
      await wait();
      const row = mockTargetReward(reward, targetUsd);
      bump();
      return row;
    },

    async setCover(req) {
      await wait();
      const result = mockSetCover(req);
      bump();
      return result;
    },

    async moveCover(subject, positionY) {
      await wait();
      const result = mockMoveCover(subject, positionY);
      bump();
      return result;
    },

    async clearCover(subject) {
      await wait();
      const result = mockClearCover(subject);
      bump();
      return result;
    },

    async setBounty(req) {
      await wait();
      const result = mockSetBounty(req);
      bump();
      return result;
    },

    async setHabitTarget(req) {
      await wait();
      const result = mockSetHabitTarget(req);
      bump();
      return result;
    },
  };
}
