import { toast } from "sonner";
import { api, unwrap } from "@/lib/live/http";
import type {
  ActivitySyncResult,
  BountyResult,
  CreatedSubject,
  HabitTargetResult,
  LifeOsWriteClient,
  OuraStatus,
  Redemption,
  RenamedSubject,
  RenamedTag,
  RewardRow,
  TimerActionResult,
  WorkoutRow,
} from "@/lib/production-ui-types";
import { normalizeOuraStatus, normalizeSync, normalizeWorkout } from "@/lib/activity";
import { renamedMessage } from "@/lib/production-ui-types";
import { normalizeTimer } from "@/lib/live/reads";
import { normalizeEarned, normalizeProgress } from "@/lib/domains";
import { foodWriteMessage, normalizeFoodWrite } from "@/lib/food";
import {
  normalizeBountyResult,
  normalizeHabitTarget,
  normalizeRedemption,
  normalizeReward,
  normalizeTimerAwards,
  redemptionMessage,
} from "@/lib/rewards";

function notify(message: string) {
  toast.success(message);
}

export function createLiveWriteClient(): LifeOsWriteClient {
  return {
    async newSubject(req) {
      const created = (await unwrap(api().POST("/api/subjects", { body: req }))) as unknown as CreatedSubject;
      notify(`Created ${created.title}`);
      return created;
    },

    async promote(source, type, title) {
      const created = (await unwrap(
        api().POST("/api/promote", { body: { source, type, title } }),
      )) as unknown as CreatedSubject;
      notify(`Promoted to ${type}`);
      return created;
    },

    async setAttributes(subject, attrs) {
      await unwrap(api().POST("/api/attributes", { body: { subject, attrs } }));
      notify("Saved");
    },

    async rename(subject, title) {
      const renamed = (await unwrap(
        api().POST("/api/rename", { body: { subject, title } }),
      )) as unknown as RenamedSubject;
      if (renamed.changed) notify(renamedMessage(renamed));
      return renamed;
    },

    async setStatus(subject, status) {
      const earned = await unwrap(api().POST("/api/status", { body: { subject, status } }));
      notify(`Status → ${status}`);
      return normalizeEarned(earned);
    },

    async archive(subject) {
      await unwrap(api().POST("/api/archive", { body: { subject } }));
      notify("Archived");
    },

    async restore(subject) {
      await unwrap(api().POST("/api/restore", { body: { subject } }));
      notify("Restored");
    },

    async tag(item, opts) {
      await unwrap(
        api().POST("/api/tag", {
          body: { item, add: opts.add, remove: opts.remove },
        }),
      );
    },

    async renameTag(from, to) {
      const renamed = (await unwrap(
        api().POST("/api/rename-tag", { body: { from, to } }),
      )) as unknown as RenamedTag;
      if (renamed.renamed + renamed.merged > 0) notify(`Renamed #${renamed.from} → #${renamed.to}`);
      return renamed;
    },

    async link(from, relation, to) {
      await unwrap(api().POST("/api/link", { body: { from, relation, to } }));
      notify("Linked");
    },

    async relate(eventId, subject, as) {
      await unwrap(
        api().POST("/api/relate", {
          body: { eventId, subject, as },
        }),
      );
      notify("Related");
    },

    async flag(item) {
      await unwrap(api().POST("/api/flag", { body: { item } }));
      notify("Flagged to Inbox");
    },

    async dismiss(item) {
      await unwrap(api().POST("/api/dismiss", { body: { item } }));
      // No toast here: the Inbox shows "Dismissed" with an Undo action.
    },

    async drop(item) {
      await unwrap(api().POST("/api/drop", { body: { item } }));
      notify("Dropped");
    },

    async adhere(habit, state, opts) {
      const earned = await unwrap(
        api().POST("/api/adhere", {
          body: { habit, state, on: opts?.on, note: opts?.note },
        }),
      );
      notify(
        state === "followed"
          ? "Marked followed"
          : state === "partial"
            ? "Partial credit"
            : "Marked missed",
      );
      return normalizeEarned(earned);
    },

    async settleDomains() {
      return normalizeEarned(await unwrap(api().POST("/api/domains/settle")));
    },

    async logFood(req) {
      const result = normalizeFoodWrite(await unwrap(api().POST("/api/food/meals", { body: req })));
      notify(foodWriteMessage(result));
      return result;
    },

    async editFood(meal, req) {
      const result = normalizeFoodWrite(
        await unwrap(api().POST("/api/food/meals/{id}", { params: { path: { id: meal } }, body: req })),
      );
      notify(foodWriteMessage(result));
      return result;
    },

    async deleteFood(meal) {
      const result = normalizeFoodWrite(
        await unwrap(api().POST("/api/food/meals/{id}/delete", { params: { path: { id: meal } } })),
      );
      notify(foodWriteMessage(result));
      return result;
    },

    async setFoodPhoto(meal, photo, filename) {
      const form = new FormData();
      form.append("photo", photo, filename ?? "photo.jpg");
      const result = normalizeFoodWrite(
        await unwrap(
          api().POST("/api/food/meals/{id}/photo", {
            params: { path: { id: meal } },
            // The browser sets the multipart boundary; openapi-fetch leaves FormData as it is.
            body: form as unknown as { photo?: string },
            bodySerializer: (body) => body as unknown as FormData,
          }),
        ),
      );
      notify(foodWriteMessage(result));
      return result;
    },

    async removeFoodPhoto(meal) {
      const result = normalizeFoodWrite(
        await unwrap(api().POST("/api/food/meals/{id}/photo/delete", { params: { path: { id: meal } } })),
      );
      notify(foodWriteMessage(result));
      return result;
    },

    async fast(req) {
      const result = normalizeFoodWrite(await unwrap(api().POST("/api/food/fasts", { body: req })));
      notify(foodWriteMessage(result));
      return result;
    },

    async closeFoodDay(day) {
      const result = normalizeFoodWrite(
        await unwrap(api().POST("/api/food/close", { body: day ? { date: day } : {} })),
      );
      notify(foodWriteMessage(result));
      return result;
    },

    async settleFood() {
      return normalizeEarned(await unwrap(api().POST("/api/food/settle")));
    },

    async authorizeOura(redirectUri) {
      const result = (await unwrap(
        api().POST("/api/oura/authorize", { body: { redirectUri } }),
      )) as unknown as { authorizeUrl: string };
      return result.authorizeUrl;
    },

    async connectOura(code, state) {
      return normalizeOuraStatus(
        (await unwrap(api().POST("/api/oura/connect", { body: { code, state } }))) as unknown as OuraStatus,
      );
    },

    async syncActivity(force) {
      return normalizeSync(
        (await unwrap(api().POST("/api/activity/sync", { body: { force: force ?? false } }))) as unknown as ActivitySyncResult,
      );
    },

    async setWorkoutNote(workout, text) {
      return normalizeWorkout(
        (await unwrap(
          api().POST("/api/activity/workouts/{id}/note", { params: { path: { id: workout } }, body: { text } }),
        )) as unknown as WorkoutRow,
      );
    },

    async timer(task, action) {
      const result = (await unwrap(
        api().POST("/api/timer", { body: { task, action } }),
      )) as unknown as TimerActionResult;
      return normalizeTimerAwards({
        ...result,
        timer: normalizeTimer(result.timer),
        progress: normalizeProgress(result.progress),
      });
    },

    async involve(subject, person, role, remove) {
      await unwrap(
        api().POST("/api/involve", { body: { subject, person, role, remove } }),
      );
    },

    async appendJournal(subject, text) {
      await unwrap(api().POST("/api/journal", { body: { subject, text } }));
      notify("Journal appended");
    },

    async capture(text) {
      await unwrap(api().POST("/api/capture", { body: { text } }));
      notify("Captured to Inbox");
    },

    async captureDocument(_req) {
      // TODO(api): multipart POST /api/capture/document — kernel `bsk attach`.
      void _req;
      toast.error("Document capture is not on the live API yet.");
      throw new Error("Document capture is not on the live API yet.");
    },

    async captureVoice(_req) {
      // TODO(api): multipart POST /api/capture/voice — kernel `bsk voice`.
      void _req;
      toast.error("Voice capture is not on the live API yet.");
      throw new Error("Voice capture is not on the live API yet.");
    },

    async recur(_habit, _spec) {
      // TODO(api): POST /api/habits/{id}/recur — kernel has no recurrence verb yet.
      toast.error("Recurrence is not on the live API yet.");
    },

    async saveReview(_id, _body) {
      // TODO(api): Review subjects (D3) are not on the live API yet.
      toast.error("Reviews are mock-only until the kernel ships.");
    },

    async completeReview(_id) {
      // TODO(api): POST /api/reviews/{id}/complete
      toast.error("Reviews are mock-only until the kernel ships.");
    },

    async redeem(req) {
      const redemption = normalizeRedemption(
        (await unwrap(api().POST("/api/redeem", { body: req }))) as unknown as Redemption,
      );
      notify(redemptionMessage(redemption));
      return redemption;
    },

    async addReward(req) {
      const reward = normalizeReward(
        (await unwrap(api().POST("/api/rewards", { body: req }))) as unknown as RewardRow,
      );
      notify(`Added ${reward.name}`);
      return reward;
    },

    async priceReward(reward, price) {
      const row = normalizeReward(
        (await unwrap(
          api().POST("/api/rewards/price", { body: { reward, price } }),
        )) as unknown as RewardRow,
      );
      notify("Price saved");
      return row;
    },

    async targetReward(reward, targetUsd) {
      const row = normalizeReward(
        (await unwrap(
          api().POST("/api/rewards/target", { body: { reward, targetUsd } }),
        )) as unknown as RewardRow,
      );
      notify(targetUsd === null ? "Target removed" : "Target saved");
      return row;
    },

    async setBounty(req) {
      const result = normalizeBountyResult(
        (await unwrap(api().POST("/api/bounty", { body: req }))) as unknown as BountyResult,
      );
      notify("Bounty saved");
      return result;
    },

    async setHabitTarget(req) {
      const result = normalizeHabitTarget(
        (await unwrap(api().POST("/api/habits/target", { body: req }))) as unknown as HabitTargetResult,
      );
      notify(req.clear ? "Target back on the default" : "Target saved");
      return result;
    },
  };
}
