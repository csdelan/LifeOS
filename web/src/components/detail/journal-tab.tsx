import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Separator } from "@/components/ui/separator";
import { EmptyState } from "@/components/primitives/empty-state";
import { ReadFailed } from "@/components/primitives/read-state";
import { ListSkeleton } from "@/components/primitives/skeletons";
import { JournalEditor } from "@/components/journal/journal-editor";
import { looksLikeHtml, sanitizeJournalHtml } from "@/components/journal/journal-html";
import { useServerPages } from "@/components/primitives/use-paged";
import { useJournal, useWrites } from "@/lib/queries";
import { formatTimestamp } from "@/lib/dates";

/** How many journal entries the tab reads at first, and how many more each Show older (code review CR121). */
export const JOURNAL_PAGE = 20;

export function JournalTab({ subjectId }: { subjectId: string }) {
  const pages = useServerPages(subjectId, JOURNAL_PAGE);
  const journal = useJournal(subjectId, pages.limit);
  const { data, isLoading } = journal;
  const writes = useWrites();
  const [composing, setComposing] = useState(false);

  return (
    <div className="space-y-4">
      {isLoading ? <ListSkeleton rows={3} /> : null}
      {!isLoading && journal.isError && !data?.length ? (
        <ReadFailed read={journal} what="The journal" />
      ) : !isLoading && (data ?? []).length === 0 ? (
        <EmptyState title="No journal yet" description="Entries are append-only. A correction is a new entry." />
      ) : !isLoading ? (
        <ol className="space-y-3">
          {[...(data ?? [])].reverse().map((e) => (
            <li key={e.eventId} className="rounded-lg bg-muted/50 px-3 py-2">
              <p className="text-[0.6875rem] text-muted-foreground">
                {formatTimestamp(e.occurredAt)}
              </p>
              {looksLikeHtml(e.content) ? (
                <div
                  className="journal-prose mt-1 text-sm"
                  dangerouslySetInnerHTML={{ __html: sanitizeJournalHtml(e.content) }}
                />
              ) : (
                <p className="mt-1 whitespace-pre-wrap text-sm">{e.content}</p>
              )}
            </li>
          ))}
        </ol>
      ) : null}
      {data && pages.full(data.length) ? (
        <Button variant="ghost" size="xs" onClick={pages.more}>
          Show older
        </Button>
      ) : null}
      <Separator />
      {composing ? (
        <JournalEditor
          onAppend={async (html) => {
            await writes.appendJournal(subjectId, html);
            setComposing(false);
          }}
        />
      ) : (
        <Button variant="outline" size="sm" onClick={() => setComposing(true)}>
          Append
        </Button>
      )}
    </div>
  );
}
