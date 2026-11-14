import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Tag } from "@/components/primitives/tag";
import { TagInput } from "@/components/primitives/tag-input";
import { useSubjectTags, useTagUniverse, useWrites } from "@/lib/queries";
import { splitTags } from "@/lib/tag-suggest";
import { useWriteAction } from "@/lib/write-action";

export function TagsTab({ subjectId }: { subjectId: string }) {
  const { data: tags } = useSubjectTags(subjectId);
  const { data: universe } = useTagUniverse();
  const writes = useWrites();
  const reported = useWriteAction({ report: "toast" });
  const [draft, setDraft] = useState("");
  const [renaming, setRenaming] = useState<string | null>(null);
  const [renameDraft, setRenameDraft] = useState("");
  const usedOn = (universe ?? []).find((u) => u.tag === renaming)?.itemCount;

  function addTags(add: string[]) {
    if (add.length === 0) return;
    void reported.run(() => writes.tag(subjectId, { add }));
    setDraft("");
  }

  async function submitRename() {
    if (!renaming) return;
    const next = renameDraft.trim();
    if (!next || next.toLowerCase() === renaming) {
      setRenaming(null);
      return;
    }
    await reported.run(() => writes.renameTag(renaming, next), {
      fallback: "Could not rename the tag.",
      onDone: () => setRenaming(null),
    });
  }

  return (
    <div className="space-y-4 rounded-xl border border-dashed border-border p-3">
      <div>
        <h3 className="type-scale-section text-muted-foreground">Tags</h3>
        <p className="mt-1 text-xs text-muted-foreground">
          Classification, not structure. Relationships live on the other tab (GEN-1).
        </p>
      </div>
      <div className="flex flex-wrap gap-1.5">
        {(tags ?? []).length === 0 ? (
          <p className="text-sm text-muted-foreground">No tags yet.</p>
        ) : (
          (tags ?? []).map((t) => (
            <Tag
              key={t}
              onRename={() => {
                setRenaming(t);
                setRenameDraft(t);
              }}
              onRemove={() => void reported.run(() => writes.tag(subjectId, { remove: [t] }))}
            >
              {t}
            </Tag>
          ))
        )}
      </div>
      {renaming ? (
        <form
          className="space-y-2 rounded-lg border border-border p-3"
          onSubmit={(e) => {
            e.preventDefault();
            void submitRename();
          }}
        >
          <p className="text-sm">
            Rename <span className="font-medium">#{renaming}</span> everywhere it's used
            {usedOn ? ` (${usedOn} item${usedOn === 1 ? "" : "s"})` : ""}.
          </p>
          <div className="flex gap-2">
            <TagInput
              autoFocus
              value={renameDraft}
              onValueChange={setRenameDraft}
              exclude={[renaming]}
              fillOnPick
              placeholder="New tag name"
              aria-label="New tag name"
            />
            <Button type="submit" size="sm">
              Rename
            </Button>
            <Button type="button" variant="outline" size="sm" onClick={() => setRenaming(null)}>
              Cancel
            </Button>
          </div>
          <p className="text-xs text-muted-foreground">
            If the new name is already a tag, the two merge into one.
          </p>
        </form>
      ) : null}
      <div className="flex gap-2">
        <TagInput
          value={draft}
          onValueChange={setDraft}
          exclude={tags ?? []}
          onCommit={addTags}
          placeholder="Add a tag"
          aria-label="Add a tag"
        />
        <Button type="button" size="sm" disabled={!splitTags(draft).length} onClick={() => addTags(splitTags(draft))}>
          Add
        </Button>
      </div>
    </div>
  );
}
