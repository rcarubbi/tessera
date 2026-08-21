"use client";

import { forwardRef, useCallback } from "react";
import { GraphCanvas, Icon, Sphere } from "reagraph";
import type { GraphCanvasProps, GraphCanvasRef, NodeRenderer } from "reagraph";

const TesseraGraphCanvas = forwardRef<GraphCanvasRef, GraphCanvasProps>(
  function TesseraGraphCanvas(props, ref) {
    const renderNode = useCallback<NodeRenderer>(
      ({ color, id, size, opacity, animated, selected, active, node }) => (
        <>
          <Sphere
            id={id}
            color={color}
            size={size}
            opacity={opacity}
            animated={animated}
            selected={selected}
            active={active}
            node={node}
          />
          <Icon
            id={id}
            image={typeof node.icon === "string" ? node.icon : ""}
            color={color}
            size={size * 0.85}
            opacity={opacity}
            animated={animated}
            selected={selected}
            active={active}
            node={node}
          />
        </>
      ),
      [],
    );

    return <GraphCanvas ref={ref} {...props} renderNode={renderNode} />;
  },
);

export default TesseraGraphCanvas;
