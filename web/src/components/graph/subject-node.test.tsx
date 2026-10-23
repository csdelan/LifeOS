import { render } from "@testing-library/react";
import { ReactFlowProvider, type NodeProps } from "@xyflow/react";
import { describe, expect, it } from "vitest";
import { SubjectGraphNode, type SubjectFlowNode } from "@/components/graph/subject-node";
import type { CoverRead, SubjectListItem } from "@/lib/production-ui-types";

const cover: CoverRead = {
  source: "unsplash",
  photoId: "abc",
  url: "https://images.unsplash.com/photo-1?ixid=x",
  color: "#334455",
  width: 3000,
  height: 2000,
  alt: null,
  photographer: "Annie Spratt",
  photographerLink: "https://unsplash.com/@anniespratt?utm_source=lifeos&utm_medium=referral",
  photoLink: "https://unsplash.com/photos/abc?utm_source=lifeos&utm_medium=referral",
  unsplashLink: "https://unsplash.com/?utm_source=lifeos&utm_medium=referral",
  positionY: 70,
  setAt: "2026-10-23T18:00:00Z",
};

const goal: SubjectListItem = {
  id: "g",
  urn: "urn:bsk:goal:g",
  type: "Goal",
  title: "Hike the Colorado Trail",
  status: "Active",
  archived: false,
  createdAt: "2026-01-01T00:00:00.000Z",
};

function renderNode(subject: SubjectListItem) {
  const props = {
    id: subject.id,
    data: { subject, degree: 1, colorByArea: false, areaColor: "" },
    selected: false,
  } as unknown as NodeProps<SubjectFlowNode>;
  return render(
    <ReactFlowProvider>
      <SubjectGraphNode {...props} />
    </ReactFlowProvider>,
  );
}

describe("SubjectGraphNode covers (COVER-3)", () => {
  it("shows the cover in the art slot under a scrim, white words, and the credit as its tooltip", () => {
    const { container } = renderNode({ ...goal, cover });
    const node = container.querySelector(".lifeos-node")!;
    expect(node).toHaveClass("has-cover");
    expect(container.querySelector(".lifeos-node__shell")).toHaveAttribute(
      "title",
      "Photo by Annie Spratt on Unsplash",
    );
    const art = container.querySelector(".lifeos-node__art")!;
    const img = art.querySelector("img")!;
    expect(img).toHaveAttribute("alt", "");
    expect(img.style.objectPosition).toBe("50% 70%");
    expect(art.querySelector(".lifeos-node__scrim")).toBeInTheDocument();
    // The type's drawing steps aside for the photo.
    expect(art.querySelector("svg")).not.toBeInTheDocument();
    expect(container.querySelector(".lifeos-node__body span.text-white")).toHaveTextContent("Goal");
  });

  it("keeps the type's drawing without a cover", () => {
    const { container } = renderNode(goal);
    expect(container.querySelector(".lifeos-node")).not.toHaveClass("has-cover");
    expect(container.querySelector(".lifeos-node__shell")).not.toHaveAttribute("title");
    expect(container.querySelector(".lifeos-node__art svg")).toBeInTheDocument();
    expect(container.querySelector(".lifeos-node__art img")).not.toBeInTheDocument();
  });
});
