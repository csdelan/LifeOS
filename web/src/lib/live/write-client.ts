import { toast } from "sonner";
import { fits } from "@/lib/live/fits";
import { api, unwrap } from "@/lib/live/http";
import type {
  LabVisitResult,
  MetricReadingResult,
  MetricReadingsResult,
  MetricRow,
  ActivitySyncResult,
  BountyResult,
  CoverCleared,
  CoverResult,
  CreatedSubject,
  HabitTargetResult,
  LifeOsWriteClient,
  MaterializeResult,
  OuraStatus,
  Redemption,
  RenamedSubject,
  RenamedTag,
  RewardRow,
  SpaceRow,
  TimerActionResult,
  WorkoutRow,
} from "@/lib/production-ui-types";
import { normalizeOuraStatus, normalizeSync, normalizeWorkout } from "@/lib/activity";
import { normalizeCover } from "@/lib/cover";
import { renamedMessage } from "@/lib/production-ui-types";
import { normalizeTimer } from "@/lib/live/reads";
import { normalizeReview } from "@/lib/reviews";
import {
  exceptionMarkedMessage,
  exceptionUnmarkedMessage,
  normalizeEarned,
  normalizeExceptionResult,
  normalizeProgress,
} from "@/lib/domains";
import { foodWriteMessage, normalizeFoodWrite } from "@/lib/food";
import {
  normalizeSpace,
  normalizeSpacePhotoResult,
  normalizeSpaceRatingResult,
  ratedMessage,
} from "@/lib/environment";
import { normalizeLabVisitResult } from "@/lib/labs";
import { normalizeMetric, normalizeReadingResult, normalizeReadingsResult } from "@/lib/measurements";
import { extensionForMime, normalizeBinaryCapture } from "@/lib/artifacts";
import { materializedMessage } from "@/lib/appointments";
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

/** A cover set or moved, its cover normalized; a download count that failed is said, not hidden. */
function coverResult(result: CoverResult): CoverResult {
  const cover = normalizeCover(result.cover);
  if (!cover) throw new Error("The server's answer had no cover in it.");
  return { ...result, cover };
}

export function createLiveWriteClient(): LifeOsWriteClient {
  return {
    async newSubject(req) {
      const created = fits<CreatedSubject>()(await unwrap(api().POST("/api/subjects", { body: req })));
      notify(`Created ${created.title}`);
      return created;
    },

    async promote(source, type, title) {
      const created = fits<CreatedSubject>()(await unwrap(
        api().POST("/api/promote", { body: { source, type, title } }),
      ));
      notify(`Promoted to ${type}`);
      return created;
    },

    async setAttributes(subject, attrs) {
      await unwrap(api().POST("/api/attributes", { body: { subject, attrs } }));
      notify("Saved");
    },

    async rename(subject, title) {
      const renamed = fits<RenamedSubject>()(await unwrap(
        api().POST("/api/rename", { body: { subject, title } }),
      ));
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
      const renamed = fits<RenamedTag>()(await unwrap(
        api().POST("/api/rename-tag", { body: { from, to } }),
      ));
      if (renamed.renamed + renamed.merged > 0) notify(`Renamed #${renamed.from} → #${renamed.to}`);
      return renamed;
    },

    async link(from, relation, to, remove) {
      await unwrap(api().POST("/api/link", { body: { from, relation, to, remove } }));
      notify(remove ? "Unlinked" : "Linked");
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

    async markException(comment, day) {
      const result = normalizeExceptionResult(
        await unwrap(api().POST("/api/domains/exceptions", { body: { day: day ?? null, comment } })),
      );
      notify(exceptionMarkedMessage(result.exception?.day ?? day));
      return result;
    },

    async unmarkException(day) {
      const result = normalizeExceptionResult(
        await unwrap(api().POST("/api/domains/exceptions/{day}/unmark", { params: { path: { day } } })),
      );
      notify(exceptionUnmarkedMessage(day));
      return result;
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
      const result = fits<{ authorizeUrl: string }>()(await unwrap(
        api().POST("/api/oura/authorize", { body: { redirectUri } }),
      ));
      return result.authorizeUrl;
    },

    async connectOura(code, state) {
      return normalizeOuraStatus(
        fits<OuraStatus>()(await unwrap(api().POST("/api/oura/connect", { body: { code, state } }))),
      );
    },

    async syncActivity(force) {
      return normalizeSync(
        fits<ActivitySyncResult>()(await unwrap(api().POST("/api/activity/sync", { body: { force: force ?? false } }))),
      );
    },

    async setWorkoutNote(workout, text) {
      return normalizeWorkout(
        fits<WorkoutRow>()(await unwrap(
          api().POST("/api/activity/workouts/{id}/note", { params: { path: { id: workout } }, body: { text } }),
        )),
      );
    },

    async addSpace(req) {
      const row = normalizeSpace(
        fits<SpaceRow>()(await unwrap(api().POST("/api/environment/spaces", { body: req }))),
      );
      notify(`Added ${row.name}`);
      return row;
    },

    async setSpace(space, req) {
      const row = normalizeSpace(
        fits<SpaceRow>()(await unwrap(
          api().POST("/api/environment/spaces/{id}", { params: { path: { id: space } }, body: req }),
        )),
      );
      notify(`Saved ${row.name}`);
      return row;
    },

    async rateSpaces(ratings, day) {
      const result = normalizeSpaceRatingResult(
        await unwrap(api().POST("/api/environment/ratings", { body: { day: day ?? null, ratings } })),
      );
      notify(ratedMessage(result));
      return result;
    },

    async editSpaceRating(rating, req) {
      const result = normalizeSpaceRatingResult(
        await unwrap(api().POST("/api/environment/ratings/{id}", { params: { path: { id: rating } }, body: req })),
      );
      notify("Rating saved");
      return result;
    },

    async deleteSpaceRating(rating) {
      const result = normalizeSpaceRatingResult(
        await unwrap(api().POST("/api/environment/ratings/{id}/delete", { params: { path: { id: rating } } })),
      );
      notify("Rating deleted");
      return result;
    },

    async addSpacePhoto(space, photo, req) {
      const form = new FormData();
      form.append("photo", photo, req?.filename ?? "photo.jpg");
      if (req?.day) form.append("day", req.day);
      if (req?.shot) form.append("shot", req.shot);
      // No toast here: a batch of photos is announced once, by the sheet that sends it.
      return normalizeSpacePhotoResult(
        await unwrap(
          api().POST("/api/environment/spaces/{id}/photos", {
            params: { path: { id: space } },
            // The browser sets the multipart boundary; openapi-fetch leaves FormData as it is.
            body: form as unknown as { photo?: string; day?: string; shot?: string },
            bodySerializer: (body) => body as unknown as FormData,
          }),
        ),
      );
    },

    async changeSpacePhoto(photo, req, opts) {
      const result = normalizeSpacePhotoResult(
        await unwrap(api().POST("/api/environment/photos/{id}", { params: { path: { id: photo } }, body: req })),
      );
      if (!opts?.quiet) notify("Photo moved");
      return result;
    },

    async removeSpacePhoto(photo) {
      const result = normalizeSpacePhotoResult(
        await unwrap(api().POST("/api/environment/photos/{id}/delete", { params: { path: { id: photo } } })),
      );
      notify("Photo removed");
      return result;
    },

    async setSpaceBaseline(photo) {
      const result = normalizeSpacePhotoResult(
        await unwrap(api().POST("/api/environment/photos/{id}/baseline", { params: { path: { id: photo } } })),
      );
      notify("Made the baseline");
      return result;
    },

    async addShot(space, name, opts) {
      const result = normalizeSpacePhotoResult(
        await unwrap(
          api().POST("/api/environment/spaces/{id}/shots", { params: { path: { id: space } }, body: { name } }),
        ),
      );
      if (!opts?.quiet) notify(`Added the shot ${name.trim()}`);
      return result;
    },

    async renameShot(space, shot, name) {
      const result = normalizeSpacePhotoResult(
        await unwrap(
          api().POST("/api/environment/spaces/{id}/shots/{shot}", {
            params: { path: { id: space, shot } },
            body: { name },
          }),
        ),
      );
      notify("Shot renamed");
      return result;
    },

    async removeShot(space, shot) {
      const result = normalizeSpacePhotoResult(
        await unwrap(
          api().POST("/api/environment/spaces/{id}/shots/{shot}/delete", { params: { path: { id: space, shot } } }),
        ),
      );
      notify("Shot removed");
      return result;
    },

    async addMetric(req) {
      const row = normalizeMetric(
        fits<MetricRow>()(await unwrap(api().POST("/api/measurements/metrics", { body: req }))),
      );
      notify(`Added ${row.name}`);
      return row;
    },

    async setMetric(metric, req) {
      const row = normalizeMetric(
        fits<MetricRow>()(await unwrap(
          api().POST("/api/measurements/metrics/{id}", { params: { path: { id: metric } }, body: req }),
        )),
      );
      notify(`Saved ${row.name}`);
      return row;
    },

    // A reading write says nothing itself: the sheet announces it with the metric's decimals.
    async logReading(req) {
      return normalizeReadingResult(
        fits<MetricReadingResult>()(await unwrap(api().POST("/api/measurements/readings", { body: req }))),
      );
    },

    async logReadings(req) {
      return normalizeReadingsResult(
        fits<MetricReadingsResult>()(await unwrap(api().POST("/api/measurements/readings/batch", { body: req }))),
      );
    },

    async editReading(reading, req) {
      return normalizeReadingResult(
        fits<MetricReadingResult>()(await unwrap(
          api().POST("/api/measurements/readings/{id}", { params: { path: { id: reading } }, body: req }),
        )),
      );
    },

    async deleteReading(reading) {
      return normalizeReadingResult(
        fits<MetricReadingResult>()(await unwrap(
          api().POST("/api/measurements/readings/{id}/delete", { params: { path: { id: reading } } }),
        )),
      );
    },

    // A visit write says nothing itself either: the sheet announces it once its report is in.
    async logLabVisit(req) {
      return normalizeLabVisitResult(
        fits<LabVisitResult>()(await unwrap(api().POST("/api/measurements/visits", { body: req }))),
      );
    },

    async saveLabVisit(visit, req) {
      return normalizeLabVisitResult(
        fits<LabVisitResult>()(await unwrap(
          api().POST("/api/measurements/visits/{id}", { params: { path: { id: visit } }, body: req }),
        )),
      );
    },

    async deleteLabVisit(visit) {
      return normalizeLabVisitResult(
        fits<LabVisitResult>()(await unwrap(
          api().POST("/api/measurements/visits/{id}/delete", { params: { path: { id: visit } } }),
        )),
      );
    },

    async attachLabReport(visit, report, filename) {
      const form = new FormData();
      form.append("report", report, filename ?? (report instanceof File ? report.name : "report.pdf"));
      return normalizeLabVisitResult(
        fits<LabVisitResult>()(await unwrap(
          api().POST("/api/measurements/visits/{id}/report", {
            params: { path: { id: visit } },
            // The browser sets the multipart boundary; openapi-fetch leaves FormData as it is.
            body: form as unknown as { report?: string },
            bodySerializer: (body) => body as unknown as FormData,
          }),
        )),
      );
    },

    async removeLabReport(visit) {
      return normalizeLabVisitResult(
        fits<LabVisitResult>()(await unwrap(
          api().POST("/api/measurements/visits/{id}/report/delete", { params: { path: { id: visit } } }),
        )),
      );
    },

    async timer(task, action) {
      const result = fits<TimerActionResult>()(await unwrap(
        api().POST("/api/timer", { body: { task, action } }),
      ));
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

    async capture(text, type) {
      await unwrap(api().POST("/api/capture", { body: { text, type } }));
      notify("Captured to Inbox");
    },

    async captureDocument(req) {
      // CAP-4: one file per capture; the API names an Idea or Problem from the description.
      const form = new FormData();
      form.append("file", req.file, req.file.name || "attachment");
      const description = req.description.trim();
      if (description) form.append("description", description);
      form.append("type", req.type);
      const result = normalizeBinaryCapture(
        await unwrap(
          api().POST("/api/capture/document", {
            // The browser sets the multipart boundary; openapi-fetch leaves FormData as it is.
            body: form as unknown as { description?: string; type?: string },
            bodySerializer: (body) => body as unknown as FormData,
          }),
        ),
      );
      notify(result.deduplicated ? "That file is already in LifeOS" : "Captured to Inbox");
      return result;
    },

    async captureVoice(req) {
      // CAP-2: the transcript only, unless Keep audio is on or there's no transcript to keep,
      // so the recording is sent only when it will be stored.
      const transcript = req.transcript.trim();
      const keep = req.keepAudio || !transcript;
      const form = new FormData();
      if (keep) {
        const type = req.audioBlob.type || "audio/webm";
        form.append("audio", req.audioBlob, `voice.${extensionForMime(type)}`);
      }
      if (transcript) form.append("transcript", transcript);
      form.append("keepAudio", String(keep));
      form.append("type", req.type);
      if (req.concerning) form.append("concerning", req.concerning);
      const result = normalizeBinaryCapture(
        await unwrap(
          api().POST("/api/capture/voice", {
            body: form as unknown as {
              transcript?: string;
              keepAudio?: boolean;
              type?: string;
              concerning?: string;
            },
            bodySerializer: (body) => body as unknown as FormData,
          }),
        ),
      );
      notify(req.concerning ? "Dictated. The nightly sweep files it in your vault." : "Captured to Inbox");
      return result;
    },

    async recur(subject, spec, opts) {
      await unwrap(
        api().POST("/api/recur", {
          body: {
            subject,
            recurrence: spec
              ? {
                  freq: spec.freq,
                  on: spec.on ?? null,
                  every: spec.every ?? null,
                  unit: spec.unit ?? null,
                  day: spec.day ?? null,
                  cue: spec.cue ?? null,
                  period: spec.period ?? null,
                  from: spec.from ?? null,
                }
              : null,
            start: opts?.start ?? null,
          },
        }),
      );
    },

    async materialize(series, through) {
      const result = fits<MaterializeResult>()(await unwrap(
        api().POST("/api/appointments/{id}/materialize", {
          params: { path: { id: series } },
          body: { through },
        }),
      ));
      notify(materializedMessage(result, through));
      return result;
    },

    async settleTasks() {
      const read = fits<{ series: number; made: CreatedSubject[] }>()(await unwrap(api().POST("/api/tasks/settle")));
      return { series: read.series || 0, made: read.made ?? [] };
    },

    async openReview(kind, date) {
      return normalizeReview(await unwrap(api().POST("/api/reviews/open", { body: { kind, date } })));
    },

    async saveReview(id, body) {
      // Keep focus only: the answers are a vault note the review links to (reviews plan §4.1).
      await unwrap(
        api().POST("/api/reviews/{id}", {
          params: { path: { id } },
          body: { keepFocus: body.keepFocus },
        }),
      );
      notify("Review saved");
    },

    async completeReview(id) {
      await unwrap(api().POST("/api/reviews/{id}/complete", { params: { path: { id } } }));
      notify("Review complete");
    },

    async redeem(req) {
      const redemption = normalizeRedemption(
        fits<Redemption>()(await unwrap(api().POST("/api/redeem", { body: req }))),
      );
      notify(redemptionMessage(redemption));
      return redemption;
    },

    async addReward(req) {
      const reward = normalizeReward(
        fits<RewardRow>()(await unwrap(api().POST("/api/rewards", { body: req }))),
      );
      notify(`Added ${reward.name}`);
      return reward;
    },

    async priceReward(reward, price) {
      const row = normalizeReward(
        fits<RewardRow>()(await unwrap(
          api().POST("/api/rewards/price", { body: { reward, price } }),
        )),
      );
      notify("Price saved");
      return row;
    },

    async targetReward(reward, targetUsd) {
      const row = normalizeReward(
        fits<RewardRow>()(await unwrap(
          api().POST("/api/rewards/target", { body: { reward, targetUsd } }),
        )),
      );
      notify(targetUsd === null ? "Target removed" : "Target saved");
      return row;
    },

    async setBounty(req) {
      const result = normalizeBountyResult(
        fits<BountyResult>()(await unwrap(api().POST("/api/bounty", { body: req }))),
      );
      notify("Bounty saved");
      return result;
    },

    async setCover(req) {
      const result = coverResult(fits<CoverResult>()(await unwrap(api().POST("/api/cover", { body: req }))));
      notify("Cover set");
      if (result.downloadProblem) toast.warning(result.downloadProblem);
      return result;
    },

    async moveCover(subject, positionY) {
      const result = coverResult(
        fits<CoverResult>()(await unwrap(api().POST("/api/cover/position", { body: { subject, positionY } }))),
      );
      notify("Cover moved");
      return result;
    },

    async clearCover(subject) {
      const result = fits<CoverCleared>()(await unwrap(api().POST("/api/cover/clear", { body: { subject } })));
      notify(result.removed ? "Cover removed" : "No cover to remove");
      return result;
    },

    async setHabitTarget(req) {
      const result = normalizeHabitTarget(
        fits<HabitTargetResult>()(await unwrap(api().POST("/api/habits/target", { body: req }))),
      );
      notify(req.clear ? "Target back on the default" : "Target saved");
      return result;
    },
  };
}
