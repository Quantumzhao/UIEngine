# UI Engine

<img src="/UIEngine/icon/icon_2048_MKII.png" alt="Icon" style="zoom: 25%;" />

For detailed information, please see project wiki page. 

---

## Progress

The project is still under development. 

### Phase 0

 Design and Planning

- [x] Architecture design
- [x] UI component layer and domain model layer interoperability

### Phase 1

Property Accessing (Reading and Writing)

- [x] Navigating to properties
- [x] Read and load property information
- [X] Display property value
- [X] Write property value
- [X] Object collection (`IEnumerable<T>`) support

### Phase 2

Method/Function Accessing and invoking

- [X] Read and load function information
- [X] load function signature and return type
- [X] Implement function parameters and their initialization
- [X] Integrate function return value into the property accessing system

### Phase 3

LINQ Accessing and invoking

- [X] LINQ expression Tree
- [ ] Integration with method and property accessing system

### Phase 4

Finalization and Optimization

- [ ] Asynchronization and parallelization
- [ ] Complete data view support
- [ ] Data view and layout saving functionality
- [ ] And others...

### Miscellaneous

#### An Implementation Using *WPF*

- [X] Components for `ObjectNode`
- [X] Components for Collections (`IColection`)
- [X] Components for Methods/Functions
- [ ] Components for LINQ
- [ ] Garbage Collection