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
import { normalizeTimer } from "@/lib/live/reads";
import { normalizeReview } from "@/lib/reviews";
import { normalizeEarned, normalizeExceptionResult, normalizeProgress } from "@/lib/domains";
import { normalizeFoodWrite } from "@/lib/food";
import { normalizeSpace, normalizeSpacePhotoResult, normalizeSpaceRatingResult } from "@/lib/environment";
import { normalizeLabVisitResult } from "@/lib/labs";
import { normalizeMetric, normalizeReadingResult, normalizeReadingsResult } from "@/lib/measurements";
import { extensionForMime, normalizeBinaryCapture } from "@/lib/artifacts";
import {
  normalizeBountyResult,
  normalizeHabitTarget,
  normalizeRedemption,
  normalizeReward,
  normalizeTimerAwards,
} from "@/lib/rewards";

/** A cover set or moved, its cover normalized; a download count that failed is said, not hidden. */
function coverResult(result: CoverResult): CoverResult {
  const cover = normalizeCover(result.cover);
  if (!cover) throw new Error("The server's answer had no cover in it.");
  return { ...result, cover };
}

export function createLiveWriteClient(): LifeOsWriteClient {
  return {
    async newSubject(req) {
      return fits<CreatedSubject>()(await unwrap(api().POST("/api/subjects", { body: req })));
    },

    async promote(source, type, title) {
      return fits<CreatedSubject>()(await unwrap(
        api().POST("/api/promote", { body: { source, type, title } }),
      ));
    },

    async setAttributes(subject, attrs) {
      await unwrap(api().POST("/api/attributes", { body: { subject, attrs } }));
    },

    async rename(subject, title) {
      return fits<RenamedSubject>()(await unwrap(
        api().POST("/api/rename", { body: { subject, title } }),
      ));
    },

    async setStatus(subject, status) {
      const earned = await unwrap(api().POST("/api/status", { body: { subject, status } }));
      return normalizeEarned(earned);
    },

    async archive(subject) {
      await unwrap(api().POST("/api/archive", { body: { subject } }));
    },

    async restore(subject) {
      await unwrap(api().POST("/api/restore", { body: { subject } }));
    },

    async tag(item, opts) {
      await unwrap(
        api().POST("/api/tag", {
          body: { item, add: opts.add, remove: opts.remove },
        }),
      );
    },

    async renameTag(from, to) {
      return fits<RenamedTag>()(await unwrap(
        api().POST("/api/rename-tag", { body: { from, to } }),
      ));
    },

    async link(from, relation, to, remove) {
      await unwrap(api().POST("/api/link", { body: { from, relation, to, remove } }));
    },

    async relate(eventId, subject, as) {
      await unwrap(
        api().POST("/api/relate", {
          body: { eventId, subject, as },
        }),
      );
    },

    async flag(item) {
      await unwrap(api().POST("/api/flag", { body: { item } }));
    },

    async dismiss(item) {
      await unwrap(api().POST("/api/dismiss", { body: { item } }));
    },

    async drop(item) {
      await unwrap(api().POST("/api/drop", { body: { item } }));
    },

    async adhere(habit, state, opts) {
      const earned = await unwrap(
        api().POST("/api/adhere", {
          body: { habit, state, on: opts?.on, note: opts?.note },
        }),
      );
      return normalizeEarned(earned);
    },

    async settleDomains() {
      return normalizeEarned(await unwrap(api().POST("/api/domains/settle")));
    },

    async markException(comment, day) {
      return normalizeExceptionResult(
        await unwrap(api().POST("/api/domains/exceptions", { body: { day: day ?? null, comment } })),
      );
    },

    async unmarkException(day) {
      return normalizeExceptionResult(
        await unwrap(api().POST("/api/domains/exceptions/{day}/unmark", { params: { path: { day } } })),
      );
    },

    async logFood(req) {
      return normalizeFoodWrite(await unwrap(api().POST("/api/food/meals", { body: req })));
    },

    async editFood(meal, req) {
      return normalizeFoodWrite(
        await unwrap(api().POST("/api/food/meals/{id}", { params: { path: { id: meal } }, body: req })),
      );
    },

    async deleteFood(meal) {
      return normalizeFoodWrite(
        await unwrap(api().POST("/api/food/meals/{id}/delete", { params: { path: { id: meal } } })),
      );
    },

    async setFoodPhoto(meal, photo, filename) {
      const form = new FormData();
      form.append("photo", photo, filename ?? "photo.jpg");
      return normalizeFoodWrite(
        await unwrap(
          api().POST("/api/food/meals/{id}/photo", {
            params: { path: { id: meal } },
            // The browser sets the multipart boundary; openapi-fetch leaves FormData as it is.
            body: form as unknown as { photo?: string },
            bodySerializer: (body) => body as unknown as FormData,
          }),
        ),
      );
    },

    async removeFoodPhoto(meal) {
      return normalizeFoodWrite(
        await unwrap(api().POST("/api/food/meals/{id}/photo/delete", { params: { path: { id: meal } } })),
      );
    },

    async fast(req) {
      return normalizeFoodWrite(await unwrap(api().POST("/api/food/fasts", { body: req })));
    },

    async closeFoodDay(day) {
      return normalizeFoodWrite(
        await unwrap(api().POST("/api/food/close", { body: day ? { date: day } : {} })),
      );
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
      return normalizeSpace(
        fits<SpaceRow>()(await unwrap(api().POST("/api/environment/spaces", { body: req }))),
      );
    },

    async setSpace(space, req) {
      return normalizeSpace(
        fits<SpaceRow>()(await unwrap(
          api().POST("/api/environment/spaces/{id}", { params: { path: { id: space } }, body: req }),
        )),
      );
    },

    async rateSpaces(ratings, day) {
      return normalizeSpaceRatingResult(
        await unwrap(api().POST("/api/environment/ratings", { body: { day: day ?? null, ratings } })),
      );
    },

    async editSpaceRating(rating, req) {
      return normalizeSpaceRatingResult(
        await unwrap(api().POST("/api/environment/ratings/{id}", { params: { path: { id: rating } }, body: req })),
      );
    },

    async deleteSpaceRating(rating) {
      return normalizeSpaceRatingResult(
        await unwrap(api().POST("/api/environment/ratings/{id}/delete", { params: { path: { id: rating } } })),
      );
    },

    async addSpacePhoto(space, photo, req) {
      const form = new FormData();
      form.append("photo", photo, req?.filename ?? "photo.jpg");
      if (req?.day) form.append("day", req.day);
      if (req?.shot) form.append("shot", req.shot);
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

    async changeSpacePhoto(photo, req) {
      return normalizeSpacePhotoResult(
        await unwrap(api().POST("/api/environment/photos/{id}", { params: { path: { id: photo } }, body: req })),
      );
    },

    async removeSpacePhoto(photo) {
      return normalizeSpacePhotoResult(
        await unwrap(api().POST("/api/environment/photos/{id}/delete", { params: { path: { id: photo } } })),
      );
    },

    async setSpaceBaseline(photo) {
      return normalizeSpacePhotoResult(
        await unwrap(api().POST("/api/environment/photos/{id}/baseline", { params: { path: { id: photo } } })),
      );
    },

    async addShot(space, name) {
      return normalizeSpacePhotoResult(
        await unwrap(
          api().POST("/api/environment/spaces/{id}/shots", { params: { path: { id: space } }, body: { name } }),
        ),
      );
    },

    async renameShot(space, shot, name) {
      return normalizeSpacePhotoResult(
        await unwrap(
          api().POST("/api/environment/spaces/{id}/shots/{shot}", {
            params: { path: { id: space, shot } },
            body: { name },
          }),
        ),
      );
    },

    async removeShot(space, shot) {
      return normalizeSpacePhotoResult(
        await unwrap(
          api().POST("/api/environment/spaces/{id}/shots/{shot}/delete", { params: { path: { id: space, shot } } }),
        ),
      );
    },

    async addMetric(req) {
      return normalizeMetric(
        fits<MetricRow>()(await unwrap(api().POST("/api/measurements/metrics", { body: req }))),
      );
    },

    async setMetric(metric, req) {
      return normalizeMetric(
        fits<MetricRow>()(await unwrap(
          api().POST("/api/measurements/metrics/{id}", { params: { path: { id: metric } }, body: req }),
        )),
      );
    },

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
    },

    async capture(text, type) {
      await unwrap(api().POST("/api/capture", { body: { text, type } }));
    },

    async captureDocument(req) {
      // CAP-4: one file per capture; the API names an Idea or Problem from the description.
      const form = new FormData();
      form.append("file", req.file, req.file.name || "attachment");
      const description = req.description.trim();
      if (description) form.append("description", description);
      form.append("type", req.type);
      return normalizeBinaryCapture(
        await unwrap(
          api().POST("/api/capture/document", {
            // The browser sets the multipart boundary; openapi-fetch leaves FormData as it is.
            body: form as unknown as { description?: string; type?: string },
            bodySerializer: (body) => body as unknown as FormData,
          }),
        ),
      );
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
      return normalizeBinaryCapture(
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
      return fits<MaterializeResult>()(await unwrap(
        api().POST("/api/appointments/{id}/materialize", {
          params: { path: { id: series } },
          body: { through },
        }),
      ));
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
    },

    async completeReview(id) {
      await unwrap(api().POST("/api/reviews/{id}/complete", { params: { path: { id } } }));
    },

    async redeem(req) {
      return normalizeRedemption(
        fits<Redemption>()(await unwrap(api().POST("/api/redeem", { body: req }))),
      );
    },

    async addReward(req) {
      return normalizeReward(
        fits<RewardRow>()(await unwrap(api().POST("/api/rewards", { body: req }))),
      );
    },

    async priceReward(reward, price) {
      return normalizeReward(
        fits<RewardRow>()(await unwrap(
          api().POST("/api/rewards/price", { body: { reward, price } }),
        )),
      );
    },

    async targetReward(reward, targetUsd) {
      return normalizeReward(
        fits<RewardRow>()(await unwrap(
          api().POST("/api/rewards/target", { body: { reward, targetUsd } }),
        )),
      );
    },

    async setBounty(req) {
      return normalizeBountyResult(
        fits<BountyResult>()(await unwrap(api().POST("/api/bounty", { body: req }))),
      );
    },

    async setCover(req) {
      return coverResult(fits<CoverResult>()(await unwrap(api().POST("/api/cover", { body: req }))));
    },

    async moveCover(subject, positionY) {
      return coverResult(
        fits<CoverResult>()(await unwrap(api().POST("/api/cover/position", { body: { subject, positionY } }))),
      );
    },

    async clearCover(subject) {
      return fits<CoverCleared>()(await unwrap(api().POST("/api/cover/clear", { body: { subject } })));
    },

    async setHabitTarget(req) {
      return normalizeHabitTarget(
        fits<HabitTargetResult>()(await unwrap(api().POST("/api/habits/target", { body: req }))),
      );
    },
  };
}
