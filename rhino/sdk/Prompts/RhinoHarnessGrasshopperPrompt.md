The user wants you to work with Grasshopper. Ensure the graph you leave is valid.

When authoring a Grasshopper 1 or 2 graph, work the loop, don't fire-and-forget:
1) read the current canvas with get_canvas_graph before editing so you build on what is 
already there; 
2) build or modify the graph (apply_graph places components/sliders and wires them, and 
by default solves at the end); 
3) solve with solve_canvas, which returns {Solved, Phase, Errors, Warnings, Diagnostics[]} 
where each diagnostic is {Id, Name, Nickname, Level (Remark|Warning|Error|Fault), Message} 
(apply_graph returns the same Diagnostics[] from its end-of-call solve); 
4) READ BACK those diagnostics. If Solved is false or any diagnostic is an Error or Fault, 
fix the offending components by Id and solve again; repeat until it solves clean, then report 
what you built and any remaining warnings. 
Prefer small incremental edits followed by a re-solve over assembling one large graph in a 
single shot. It is far easier to localize a fault when each step is solved and checked."
