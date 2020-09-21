# Manifest of Complete features

Completed and developing features. 

> - [ ] **Unchecked:** Not intended to be developed in the near future
> - [x] **Checked:** Implemented, but not not fully completed
> - [x] **~~Deleted:~~** Completely finished
> - [ ] **==Unchecked==:** Developing

## Object Node

- [x] ~~Two-way data binding~~

- [x] ~~Display binded value~~

- [x] ~~Display instance method~~

- [x] ~~Modify and assign value~~
	
- [x] ~~Type inference~~
  
- [x] template instantiation
  
- [ ] ==preview==
  
- [ ] ==unsafe accessing==
  
    > Continue display descendant template object nodes without the object is not loaded

## Method Node

- [ ] ==Display parameters with==
    - [ ] ==descriptions==
    - [ ] previews
    - [ ] complete list of candidate arguments
    - [ ] correct list of candidate arguments
- [x] Modify parameters
    - [ ] Object node assignment (drag and drop)
- [ ] Do verification (type inference) when modifying parameters
- [x] ~~Return value~~
- [x] Type inference on return value

## Collection Node

- [x] Two way collection binding
- [ ] ==Collection node assignment==

## Extension Function Node

- [ ] ==Filter==
    - [ ] ==composite expression==
- [ ] Sort
    - [ ] composite expression
- [ ] Map
    - [ ] multiple expressions, and combine them to a single table (collection)
- [ ] Foreach

## Automation

- [ ] Serialize/Deserialize chain of `ObjectNode` to code, which grammar is suggested below: 

    ``` 
    Expr ::= ObjectNodeName
           | Expr.Expr
           | Expr[Val]
           | MethodName(ExprList)
           | CollectionNodeName
           | CollectionNodeName.ExtensionFunctionNode
    Val ::= Int
    	  | String
    ExprList ::= Expr
    		  | Expr, ExprList
    BoolExpr ::= BoolExpr AND BoolExpr
    		  | BoolExpr OR BoolExpr
    		  | NOT BoolExpr
    ExtensionFunctionNode ::= Filter(TemplatedObjectNode, BoolExpr)
    					  | Sort(TemplatedObjectNode, IComparable)
    					  | Map(TemplatedObjectNode, Expr)
    					  | Foreach(TemplatedObjectNode, Expr.MethodNodeName(ExprList))
    ```

    